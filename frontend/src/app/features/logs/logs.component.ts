import { Component, OnInit, OnDestroy, NgZone, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { ScrollingModule } from '@angular/cdk/scrolling';
import { Subject, Subscription, debounceTime } from 'rxjs';

import { DiagnosticsService } from '../../core/services/diagnostics.service';
import { LogsSignalrService, LogEntry } from '../../core/services/logs-signalr.service';
import { LanguageService } from '../../core/i18n/language.service';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { KnxDatePipe } from '../../core/i18n/date.pipe';
import { ToastService } from '../../core/services/toast.service';
import { LoggerService } from '../../core/logging/logger.service';

type LevelFilter = 'All' | 'Debug' | 'Information' | 'Warning' | 'Error';

/** Cap on the in-memory buffer so a busy bus does not grow the array forever. */
const MAX_ENTRIES = 2000;

/**
 * In-app log viewer. Loads the recent backend log lines once, then streams new
 * lines live over the `/hubs/logs` SignalR hub. Filtering (level + free text)
 * and the pause toggle are applied client-side so live and historic lines obey
 * the same rules. Rendered through a CDK virtual-scroll viewport.
 */
@Component({
  selector: 'app-logs',
  imports: [CommonModule, FormsModule, MatIconModule, MatTooltipModule, ScrollingModule, TranslatePipe, KnxDatePipe],
  templateUrl: './logs.component.html',
  styleUrl: './logs.component.scss'
})
export class LogsComponent implements OnInit, OnDestroy {
  private diagnostics = inject(DiagnosticsService);
  private hub = inject(LogsSignalrService);
  private zone = inject(NgZone);
  private lang = inject(LanguageService);
  private toast = inject(ToastService);
  private logger = inject(LoggerService);

  /** Full buffer, newest first. */
  private all: LogEntry[] = [];
  /** Lines that arrived while paused, flushed on resume (newest first). */
  private pending: LogEntry[] = [];

  /** Derived, filtered view bound to the template. */
  filtered: LogEntry[] = [];

  readonly levels: LevelFilter[] = ['All', 'Debug', 'Information', 'Warning', 'Error'];
  level: LevelFilter = 'All';
  search = '';
  paused = false;
  loading = false;
  error = false;
  downloading = false;

  /** Count of buffered lines while paused (shown as a badge on Resume). */
  get pendingCount(): number { return this.pending.length; }

  private searchChanged = new Subject<void>();
  private sub = new Subscription();

  ngOnInit(): void {
    this.load();

    this.sub.add(
      this.searchChanged.pipe(debounceTime(250)).subscribe(() => this.recompute())
    );

    this.sub.add(
      this.hub.log$.subscribe(entry => {
        // Hub callbacks fire outside Angular's zone — re-enter so the view updates.
        this.zone.run(() => this.onLive(entry));
      })
    );

    this.hub.startConnection();
  }

  ngOnDestroy(): void {
    this.sub.unsubscribe();
    this.hub.stopConnection();
  }

  private load(): void {
    this.loading = true;
    this.error = false;
    // Pull a generous slice (All levels) and filter on the client.
    this.diagnostics.getLogs('All', '', 500).subscribe({
      next: entries => {
        this.all = entries.slice(0, MAX_ENTRIES);
        this.recompute();
        this.loading = false;
      },
      error: err => {
        this.logger.error('[Logs] Failed to load logs:', err);
        this.error = true;
        this.loading = false;
      }
    });
  }

  private onLive(entry: LogEntry): void {
    if (this.paused) {
      this.pending.unshift(entry);
      if (this.pending.length > MAX_ENTRIES) this.pending.length = MAX_ENTRIES;
      return;
    }
    this.all.unshift(entry);
    if (this.all.length > MAX_ENTRIES) this.all.length = MAX_ENTRIES;
    if (this.matches(entry)) this.filtered = [entry, ...this.filtered];
  }

  setLevel(l: LevelFilter): void {
    this.level = l;
    this.recompute();
  }

  onSearchInput(): void {
    this.searchChanged.next();
  }

  togglePause(): void {
    this.paused = !this.paused;
    if (!this.paused && this.pending.length) {
      // Flush buffered lines back into the main buffer, keeping newest first.
      this.all = [...this.pending, ...this.all].slice(0, MAX_ENTRIES);
      this.pending = [];
      this.recompute();
    }
  }

  clear(): void {
    this.all = [];
    this.pending = [];
    this.filtered = [];
  }

  download(): void {
    if (this.downloading) return;
    this.downloading = true;
    this.diagnostics.downloadDiagnostics().subscribe({
      next: blob => {
        this.saveBlob(blob, this.diagnosticsFileName());
        this.downloading = false;
      },
      error: err => {
        this.logger.error('[Logs] Diagnostics download failed:', err);
        this.toast.error(this.lang.translate('logs.downloadFailed'));
        this.downloading = false;
      }
    });
  }

  /** Stable identity for the virtual-scroll viewport. */
  trackEntry = (_: number, e: LogEntry): string => `${e.timestamp}|${e.level}|${e.message}`;

  /** CSS modifier for the level (drives the colour coding). */
  levelClass(level: string): string {
    return `lvl-${(level || '').toLowerCase()}`;
  }

  // --- internals -------------------------------------------------------------

  private recompute(): void {
    this.filtered = this.all.filter(e => this.matches(e));
  }

  private matches(e: LogEntry): boolean {
    if (!this.matchesLevel(e.level)) return false;
    const q = this.search.trim().toLowerCase();
    if (!q) return true;
    return (e.message ?? '').toLowerCase().includes(q)
        || (e.source ?? '').toLowerCase().includes(q);
  }

  private matchesLevel(level: string): boolean {
    if (this.level === 'All') return true;
    const l = (level || '').toLowerCase();
    switch (this.level) {
      case 'Error': return l === 'error' || l === 'fatal';
      case 'Debug': return l === 'debug' || l === 'verbose';
      case 'Warning': return l === 'warning';
      case 'Information': return l === 'information';
      default: return true;
    }
  }

  private diagnosticsFileName(): string {
    const ts = new Date().toISOString().replace(/[:.]/g, '-').slice(0, 19);
    return `knx-diagnostics-${ts}.zip`;
  }

  private saveBlob(blob: Blob, fileName: string): void {
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = fileName;
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    URL.revokeObjectURL(url);
  }
}
