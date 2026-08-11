import { Component, NgZone, OnDestroy, OnInit, inject } from '@angular/core';
import { Subscription } from 'rxjs';
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
import { DptTitleDirective } from '../../shared/grid/dpt-title.directive';
import { formatKnxDate } from '../../core/i18n/date.util';
import { LoggerService } from '../../core/logging/logger.service';
import { ConfirmDialogComponent, ConfirmDialogData } from '../../shared/confirm-dialog.component';
import { SignalrService, KnxTelegram } from '../../core/services/signalr.service';
import { LiveBufferService } from '../../core/services/live-buffer.service';
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
  /** Mobile only: the write controls start folded away so a bus write takes a deliberate second tap. */
  writeOpen?: boolean;
  busy: boolean;

  /** Most recent value seen on the bus for this address, filled from the live stream. */
  liveValue?: string;
  /** When that value arrived (epoch ms), for the tooltip. */
  liveAt?: number;
  /** A read was just sent and no telegram has come back yet. */
  pendingRead?: boolean;
  /** The read produced nothing within the wait window. */
  noAnswer?: boolean;

  /** Canonical KNX flags aggregated over the linked communication objects. */
  flags?: string[];
  /** Untouched flag strings from the project, shown in the badge tooltip. */
  flagsRaw?: string;
}

/** How long a sent read waits for a telegram before it is reported as unanswered. */
const READ_ANSWER_TIMEOUT_MS = 3000;

/**
 * Maps what the parser writes into CommunicationObject.Flags onto the canonical KNX flags.
 * ETS 5/6 store them explicitly; ETS 4 only has Send/Receive connectors, which describe the
 * device's role — sending on a group address is transmitting, receiving is being written to.
 * That mapping is an interpretation, so the raw project value stays in the tooltip.
 */
const FLAG_ALIASES: Record<string, string> = {
  Communication: 'C',
  Read: 'R',
  Write: 'W',
  Transmit: 'T',
  Update: 'U',
  ReadOnInit: 'I',
  Send: 'T',
  Receive: 'W'
};

/** Display order of the badges, following the usual KNX notation. */
const FLAG_ORDER = ['C', 'R', 'W', 'T', 'U', 'I'];

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
  imports: [CommonModule, FormsModule, MatIconModule, TranslatePipe, DptTitleDirective],
  templateUrl: './group-addresses.component.html',
  styleUrl: './group-addresses.component.scss'
})
export class GroupAddressesComponent implements OnInit, OnDestroy {
  private projectService = inject(ProjectService);
  private lang = inject(LanguageService);
  private logger = inject(LoggerService);
  private router = inject(Router);
  private http = inject(HttpClient);
  private dialog = inject(MatDialog);
  private snackBar = inject(MatSnackBar);
  private signalr = inject(SignalrService);
  private liveBuffer = inject(LiveBufferService);
  private zone = inject(NgZone);

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

  private telegramSub?: Subscription;
  private flushTimer?: ReturnType<typeof setTimeout>;
  /** Values seen since the last flush, keyed by destination address. */
  private pendingValues = new Map<string, { value: string; at: number }>();
  /** Pending read timeouts, keyed by address. */
  private readTimers = new Map<string, ReturnType<typeof setTimeout>>();
  /** Address → leaves, so an incoming telegram does not scan the whole list. */
  private leavesByAddress = new Map<string, GaLeaf[]>();

  ngOnInit(): void {
    void this.load();

    // Subscribing to telegram$ alone is not enough: the hub connection is owned by
    // LiveBufferService and would otherwise only exist once the Monitor or Graph view has been
    // opened. start() is idempotent — calling SignalrService.startConnection() directly would
    // tear down and rebuild a connection those views are already using.
    void this.liveBuffer.start();

    // Subscribed outside Angular on purpose: a busy bus would otherwise run change detection
    // for every single telegram. The buffered values are applied inside the zone by the flush
    // timer below, which is the only place the view needs to update.
    this.zone.runOutsideAngular(() => {
      this.telegramSub = this.signalr.telegram$.subscribe((t) => this.bufferLiveValue(t));
    });
  }

  ngOnDestroy(): void {
    this.telegramSub?.unsubscribe();
    if (this.flushTimer) clearTimeout(this.flushTimer);
    for (const timer of this.readTimers.values()) clearTimeout(timer);
  }

  // --- Live values -----------------------------------------------------------
  /**
   * Values are filled from the live stream only — no bulk query on load. Addresses that never
   * send simply stay empty, which is honest: nobody has reported a value for them.
   */
  private bufferLiveValue(t: KnxTelegram): void {
    const value = t.valueDecoded || t.value;
    if (!value) return;
    this.pendingValues.set(t.destinationAddress, { value, at: new Date(t.timestamp).getTime() });

    if (this.flushTimer) return;
    this.flushTimer = setTimeout(() => {
      this.flushTimer = undefined;
      this.zone.run(() => this.applyLiveValues());
    }, 400);
  }

  private applyLiveValues(): void {
    for (const [address, entry] of this.pendingValues) {
      for (const leaf of this.leavesByAddress.get(address) ?? []) {
        leaf.liveValue = entry.value;
        leaf.liveAt = entry.at;
        // A value arrived, so the read was answered after all.
        leaf.pendingRead = false;
        leaf.noAnswer = false;
        const timer = this.readTimers.get(address);
        if (timer) {
          clearTimeout(timer);
          this.readTimers.delete(address);
        }
      }
    }
    this.pendingValues.clear();
  }

  /** Absolute timestamp of the shown value, for the row tooltip. */
  liveValueTitle(leaf: GaLeaf): string | null {
    if (!leaf.liveAt) return null;
    return formatKnxDate(leaf.liveAt, 'dateTime', this.lang.lang()) || null;
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
      // A group address can legitimately appear more than once in a project, so index to a list.
      this.leavesByAddress = new Map();
      for (const leaf of this.allLeaves) {
        const bucket = this.leavesByAddress.get(leaf.address);
        if (bucket) bucket.push(leaf); else this.leavesByAddress.set(leaf.address, [leaf]);
      }
      this.groupRanges = await this.projectService.getGroupRanges(active.id).toPromise() || [];
      await this.loadFlags(active.id);
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

  /**
   * KNX communication flags are stored per communication object, not per group address. A group
   * address is described by the union of the flags of every com object linked to it — one device
   * may only send on it while another reacts to it.
   *
   * Failing to load them is not fatal: the badges simply stay away.
   */
  private async loadFlags(projectId: number): Promise<void> {
    try {
      const comObjects = await this.projectService.getCommObjects(projectId).toPromise() || [];
      const byAddress = new Map<string, { tokens: Set<string>; raw: Set<string> }>();

      for (const co of comObjects) {
        if (!co.groupAddressLink || !co.flags) continue;
        let entry = byAddress.get(co.groupAddressLink);
        if (!entry) {
          entry = { tokens: new Set(), raw: new Set() };
          byAddress.set(co.groupAddressLink, entry);
        }
        entry.raw.add(co.flags);
        for (const token of co.flags.split(',')) {
          const normalised = FLAG_ALIASES[token.trim()];
          if (normalised) entry.tokens.add(normalised);
        }
      }

      for (const leaf of this.allLeaves) {
        const entry = byAddress.get(leaf.address);
        if (!entry) continue;
        // Fixed order so the badges read the same on every row.
        leaf.flags = FLAG_ORDER.filter(f => entry.tokens.has(f));
        leaf.flagsRaw = [...entry.raw].join(' / ');
      }
    } catch (err) {
      this.logger.error('Failed to load communication object flags:', err);
    }
  }

  /** Short label of a flag badge, e.g. "L" for Lesen / "R" for Read. */
  flagLabel(flag: string): string {
    return this.lang.translate(`ga.flag.${flag}`);
  }

  /**
   * Tooltip: the flag's full name plus the raw value from the project, so the interpretation
   * stays checkable — ETS 4 stores Send/Receive connectors rather than explicit flags, and those
   * are mapped onto Transmit/Write here.
   */
  flagTitle(flag: string, leaf: GaLeaf): string {
    const name = this.lang.translate(`ga.flag.${flag}.name`);
    return leaf.flagsRaw ? `${name} · ${leaf.flagsRaw}` : name;
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
    // Collapsing folds the mobile write rows away too — otherwise expanding again brings back
    // an open write row with a value already typed into it.
    if (!open) {
      for (const leaf of this.allLeaves) leaf.writeOpen = false;
    }
  }

  get hasGroupAddresses(): boolean {
    return this.allLeaves.length > 0;
  }

  get totalCount(): number {
    return this.allLeaves.length;
  }

  // --- Bus actions (mirror telegram-detail.component.ts) ---------------------

  /**
   * Sends a GroupValueRead. The bus answers with a normal telegram, so the value shows up
   * through the live stream rather than in this response — the request itself only reports
   * that it went out. The pending flag closes that gap: if nothing arrives within the wait
   * window the row says so instead of leaving a silent read looking successful.
   */
  read(leaf: GaLeaf): void {
    leaf.busy = true;
    this.http.post<{ success: boolean }>(`${environment.apiUrl}/knx/read`, { address: leaf.address })
      .subscribe({
        next: () => {
          leaf.busy = false;
          leaf.pendingRead = true;
          leaf.noAnswer = false;
          this.armReadTimeout(leaf);
          this.toast(this.lang.translate('detail.readSent'));
        },
        error: () => { leaf.busy = false; this.toast(this.lang.translate('detail.readFailed')); }
      });
  }

  private armReadTimeout(leaf: GaLeaf): void {
    const existing = this.readTimers.get(leaf.address);
    if (existing) clearTimeout(existing);

    this.readTimers.set(leaf.address, setTimeout(() => {
      this.readTimers.delete(leaf.address);
      for (const l of this.leavesByAddress.get(leaf.address) ?? []) {
        if (l.pendingRead) {
          l.pendingRead = false;
          l.noAnswer = true;
        }
      }
    }, READ_ANSWER_TIMEOUT_MS));
  }

  /**
   * Mobile: folds a leaf's value input open or shut. On desktop the input is always visible,
   * where the CSS simply ignores the flag.
   */
  toggleWrite(leaf: GaLeaf): void {
    leaf.writeOpen = !leaf.writeOpen;
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
      // Without maxWidth, Material's 80vw default shrinks the warning dialog to 288px
      // on 360px devices and the warning text breaks apart.
      .open(ConfirmDialogComponent, { data, width: 'min(420px, calc(100vw - 32px))', maxWidth: '100vw' })
      .afterClosed()
      .toPromise();

    if (!confirmed) return;

    leaf.busy = true;
    this.http.post<{ success: boolean }>(`${environment.apiUrl}/knx/write`, {
      address: leaf.address,
      datapointType: leaf.datapointType ?? null,
      value: leaf.writeValue
    }).subscribe({
      next: () => {
        leaf.busy = false;
        leaf.writeOpen = false;
        this.toast(this.lang.translate('detail.written', { address: leaf.address, value: shown }));
      },
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
