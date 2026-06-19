import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { HttpClient } from '@angular/common/http';
import { MatIconModule } from '@angular/material/icon';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatDialog } from '@angular/material/dialog';
import { ProjectService, GroupAddressDto, GroupRangeDto } from '../../core/services/project.service';
import { LanguageService } from '../../core/i18n/language.service';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { LoggerService } from '../../core/logging/logger.service';
import { ConfirmDialogComponent, ConfirmDialogData } from '../../shared/confirm-dialog.component';
import { environment } from '../../../environments/environment.development';

/** Numeric DPT mains (plus DPT1 boolean) that can be plotted in the charts view. */
const CHARTABLE_DPT_MAINS = new Set([1, 5, 6, 7, 8, 9, 12, 13, 14]);

/** A single group address leaf, enriched with per-row write state. */
interface GaLeaf extends GroupAddressDto {
  /** Parsed main DPT number, or null when unknown. */
  dptMain: number | null;
  isBool: boolean;
  isNumeric: boolean;
  chartable: boolean;
  /** Bound value of the per-row write input (booleans default to On). */
  writeValue: string;
  busy: boolean;
}

/** Middle group (level 2): groups GAs sharing the same main/middle pair. */
interface MiddleNode {
  /** Middle group number. */
  number: number;
  /** Resolved GroupRange name for this middle block, or null when none matches. */
  name: string | null;
  leaves: GaLeaf[];
}

/** Main group (level 1). */
interface MainNode {
  /** Main group number. */
  number: number;
  /** Resolved GroupRange name for this main block, or null when none matches. */
  name: string | null;
  middles: MiddleNode[];
  /** Total GA leaves beneath this main group (across all middles). */
  count: number;
}

/**
 * Group-address tree view. Loads the active project's flat GA list and rebuilds a
 * 3-level tree (main / middle / GA leaf) from the `main/middle/sub` address. Each
 * leaf can trigger a bus Read, a DPT-typed (confirmed) Write, and a jump to the
 * chart with the GA preselected. Read/Write mirror the telegram-detail bus actions.
 */
@Component({
  selector: 'app-group-addresses',
  standalone: true,
  imports: [CommonModule, FormsModule, MatIconModule, TranslatePipe],
  templateUrl: './group-addresses.component.html',
  styleUrl: './group-addresses.component.scss'
})
export class GroupAddressesComponent implements OnInit {
  private projectService = inject(ProjectService);
  private lang = inject(LanguageService);
  private logger = inject(LoggerService);
  private router = inject(Router);
  private http = inject(HttpClient);
  private dialog = inject(MatDialog);
  private snackBar = inject(MatSnackBar);

  loading = false;
  error = false;
  hasActiveProject = false;

  /** All leaves (flat), kept for filtering. */
  private allLeaves: GaLeaf[] = [];
  /** GroupRanges (main/middle group names) of the active project. */
  private groupRanges: GroupRangeDto[] = [];
  /** The rebuilt tree (after the active filter is applied). */
  mains: MainNode[] = [];

  /** Drives the open/closed state of every <details> via a single bound flag. */
  allOpen = true;

  /** Free-text filter over address + name. */
  filter = '';

  ngOnInit(): void {
    void this.load();
  }

  async load(): Promise<void> {
    this.loading = true;
    this.error = false;
    try {
      const projects = await this.projectService.getAllProjects().toPromise() || [];
      const active = projects.find(p => p.isActive);
      this.hasActiveProject = !!active;
      if (!active) {
        this.allLeaves = [];
        this.groupRanges = [];
        this.mains = [];
        return;
      }
      const details = await this.projectService.getProjectDetails(active.id).toPromise();
      this.allLeaves = (details?.groupAddresses ?? []).map(ga => this.toLeaf(ga));
      this.groupRanges = await this.projectService.getGroupRanges(active.id).toPromise() || [];
      this.rebuild();
    } catch (err) {
      this.logger.error('Failed to load group addresses:', err);
      this.error = true;
      this.allLeaves = [];
      this.groupRanges = [];
      this.mains = [];
    } finally {
      this.loading = false;
    }
  }

  private toLeaf(ga: GroupAddressDto): GaLeaf {
    const m = (ga.datapointType ?? '').match(/\d+/);
    const dptMain = m ? parseInt(m[0], 10) : null;
    const isBool = dptMain === 1;
    const isNumeric = dptMain !== null && [5, 6, 7, 8, 9, 12, 13, 14].includes(dptMain);
    return {
      ...ga,
      dptMain,
      isBool,
      isNumeric,
      chartable: dptMain !== null && CHARTABLE_DPT_MAINS.has(dptMain),
      writeValue: isBool ? '1' : '',
      busy: false
    };
  }

  /** Apply the text filter, then rebuild the main/middle/sub tree. */
  private rebuild(): void {
    const q = this.filter.trim().toLowerCase();
    const leaves = q
      ? this.allLeaves.filter(l =>
          l.address.toLowerCase().includes(q) ||
          (l.name ?? '').toLowerCase().includes(q))
      : this.allLeaves;

    const mainMap = new Map<number, Map<number, GaLeaf[]>>();
    for (const leaf of leaves) {
      const parts = leaf.address.split('/');
      const main = Number(parts[0]);
      const middle = Number(parts[1]);
      if (!Number.isFinite(main) || !Number.isFinite(middle)) continue;
      let middles = mainMap.get(main);
      if (!middles) { middles = new Map(); mainMap.set(main, middles); }
      let arr = middles.get(middle);
      if (!arr) { arr = []; middles.set(middle, arr); }
      arr.push(leaf);
    }

    const mains: MainNode[] = [];
    for (const main of [...mainMap.keys()].sort((a, b) => a - b)) {
      const middlesMap = mainMap.get(main)!;
      const middles: MiddleNode[] = [];
      let count = 0;
      for (const middle of [...middlesMap.keys()].sort((a, b) => a - b)) {
        const leaves = middlesMap.get(middle)!
          .sort((a, b) => a.address.localeCompare(b.address, undefined, { numeric: true }));
        count += leaves.length;
        middles.push({ number: middle, name: this.middleName(main, middle), leaves });
      }
      mains.push({ number: main, name: this.mainName(main), middles, count });
    }
    this.mains = mains;
  }

  /**
   * Name of the main group `main` = the GroupRange covering the main block (2048 addrs)
   * that spans >= 256 addresses. Returns null when none matches.
   */
  private mainName(main: number): string | null {
    const base = main * 2048;
    const r = this.groupRanges.find(r =>
      r.rangeStart <= base + 2047 && r.rangeEnd >= base && (r.rangeEnd - r.rangeStart) >= 256);
    return r?.name ?? null;
  }

  /**
   * Name of the middle group `main/middle` = the GroupRange covering the middle block
   * (256 addrs) with a span < 256. Returns null when none matches.
   */
  private middleName(main: number, middle: number): string | null {
    const s = main * 2048 + middle * 256;
    const r = this.groupRanges.find(r =>
      r.rangeStart <= s + 255 && r.rangeEnd >= s && (r.rangeEnd - r.rangeStart) < 256);
    return r?.name ?? null;
  }

  onFilterChange(): void {
    this.rebuild();
  }

  setAllOpen(open: boolean): void {
    this.allOpen = open;
  }

  get hasGroupAddresses(): boolean {
    return this.allLeaves.length > 0;
  }

  get totalCount(): number {
    return this.allLeaves.length;
  }

  // --- Bus actions (mirror telegram-detail.component.ts) ---------------------

  read(leaf: GaLeaf): void {
    leaf.busy = true;
    this.http.post<{ success: boolean }>(`${environment.apiUrl}/knx/read`, { address: leaf.address })
      .subscribe({
        next: () => { leaf.busy = false; this.toast(this.lang.translate('detail.readSent')); },
        error: () => { leaf.busy = false; this.toast(this.lang.translate('detail.readFailed')); }
      });
  }

  async write(leaf: GaLeaf): Promise<void> {
    if (leaf.writeValue === '') return;

    const shown = leaf.isBool
      ? this.lang.translate(leaf.writeValue === '1' ? 'detail.on' : 'detail.off')
      : leaf.writeValue;

    const data: ConfirmDialogData = {
      title: this.lang.translate('detail.writeConfirmTitle'),
      message: this.lang.translate('detail.writeConfirmMsg', { address: leaf.address, value: shown }),
      warning: this.lang.translate('detail.writeConfirmWarning'),
      confirmText: this.lang.translate('detail.write'),
      danger: true
    };

    const confirmed = await this.dialog
      .open(ConfirmDialogComponent, { data, width: '420px' })
      .afterClosed()
      .toPromise();

    if (!confirmed) return;

    leaf.busy = true;
    this.http.post<{ success: boolean }>(`${environment.apiUrl}/knx/write`, {
      address: leaf.address,
      datapointType: leaf.datapointType ?? null,
      value: leaf.writeValue
    }).subscribe({
      next: () => { leaf.busy = false; this.toast(this.lang.translate('detail.written', { address: leaf.address, value: shown })); },
      error: () => { leaf.busy = false; this.toast(this.lang.translate('detail.writeFailed')); }
    });
  }

  openChart(leaf: GaLeaf): void {
    void this.router.navigate(['/charts'], { queryParams: { ga: leaf.address } });
  }

  private toast(message: string): void {
    this.snackBar.open(message, this.lang.translate('common.close'), {
      duration: 3000, horizontalPosition: 'end', verticalPosition: 'top'
    });
  }
}
