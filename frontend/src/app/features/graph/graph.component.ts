import {
  Component, OnInit, OnDestroy, AfterViewInit, ElementRef, ViewChild, NgZone, inject,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { Subscription, forkJoin } from 'rxjs';
import ForceGraph from 'force-graph';

import {
  ProjectService, GroupAddressDto, DeviceDto, LocationDto, CommunicationObjectDto, GroupRangeDto,
} from '../../core/services/project.service';
import { SignalrService, KnxTelegram } from '../../core/services/signalr.service';
import { LiveBufferService } from '../../core/services/live-buffer.service';
import { messageTypeKind } from '../../shared/grid/knx-grid.util';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { LanguageService } from '../../core/i18n/language.service';
import { DPT_DESCRIPTIONS } from '../../shared/grid/dpt-descriptions';
import { SearchCache, searchTokens } from '../../shared/search.util';
import { SlashFocusDirective } from '../../shared/slash-focus.directive';
import { escapeHtml } from '../../shared/html.util';

type NodeKind = 'ga' | 'group' | 'room' | 'device';

interface GNode {
  id: string;
  label: string;
  kind: NodeKind;
  hub?: 'building' | 'floor' | 'room';
  val: number;
  color: string;
  address?: string;
  cluster?: string;          // "<roomHub>|<function>" — same-function GAs clump within a room
  __glow?: number;
  __glowColor?: string;
  __heat?: number;
  __value?: string;
  x?: number; y?: number;
  fx?: number; fy?: number;
  vx?: number; vy?: number;
  _children?: GNode[];
}
interface GLink {
  source: string | GNode; target: string | GNode; __pc?: string;
  lvl?: 'bf' | 'fr' | 'rg';   // building-floor / floor-room / room-ga nesting
}

const PALETTE = [
  '#2563EB', '#16A34A', '#DC2626', '#D97706', '#9333EA',
  '#0891B2', '#DB2777', '#CA8A04', '#0D9488', '#E11D48',
  '#7C3AED', '#65A30D', '#EA580C', '#0284C7', '#BE185D',
];
const HUB_GREY = { building: '#475569', floor: '#64748B', room: '#94A3B8' };
// Tasteful, well-separated floor colours (few floors → stay distinct + calm).
const FLOOR_PALETTE = ['#3B82F6', '#10B981', '#F59E0B', '#A855F7', '#EC4899', '#14B8A6', '#EF4444'];
const LEGEND_KEY = 'knx.graph.legend';
const FILTER_KEY = 'knx.graph.filter';

/**
 * The four combinable graph filters (#17). Group, location and DPT narrow down which GAs may
 * show at all; the free text then picks hits among them and keeps `depth` link levels around
 * each hit. Building, floor and room hubs above a visible GA always stay, so nothing floats.
 */
interface GraphFilter {
  /** '' | main group "1" | middle group "1/2". */
  group: string;
  /** '' | externalId of a location; covers everything below it. */
  room: string;
  /** '' | DPT main number, e.g. "9". */
  dpt: string;
  q: string;
  /** Neighbourhood around a text hit, 1 to 3 link levels. */
  depth: number;
}

const EMPTY_FILTER: GraphFilter = { group: '', room: '', dpt: '', q: '', depth: 1 };

interface FilterOption { value: string; label: string; depth: number }

/**
 * Force-directed building map: Building → Floor → Room → GA. Floors/rooms bind
 * tight so storeys form clusters; GAs are coloured by function (group range).
 * Live telegrams make the matching GA glow + show its current value.
 */
@Component({
  selector: 'app-graph',
  standalone: true,
  imports: [CommonModule, FormsModule, MatIconModule, MatTooltipModule, TranslatePipe, SlashFocusDirective],
  templateUrl: './graph.component.html',
  styleUrl: './graph.component.scss',
})
export class GraphComponent implements OnInit, AfterViewInit, OnDestroy {
  private projectService = inject(ProjectService);
  private signalr = inject(SignalrService);
  private liveBuffer = inject(LiveBufferService);
  private router = inject(Router);
  private zone = inject(NgZone);

  @ViewChild('host') hostRef!: ElementRef<HTMLDivElement>;

  loading = false;
  hasActiveProject = false;
  nodeCount = 0;
  linkCount = 0;
  legend: { label: string; color: string }[] = [];
  /** Legende ein-/ausblendbar: auf Mobil verdeckt sie sonst ~40 % der Zeichenfläche.
   *  Gespeicherte Wahl gewinnt, sonst Viewport-Default (Desktop an, Mobil aus). */
  showLegend = GraphComponent.readLegendPref();

  // --- filter (#17) ------------------------------------------------------------
  filter: GraphFilter = GraphComponent.readFilterPref();
  /** Mobile only: the filter bar is folded behind the toolbar toggle. */
  filtersExpanded = false;
  readonly depths = [1, 2, 3];
  groupOptions: FilterOption[] = [];
  roomOptions: FilterOption[] = [];
  dptOptions: string[] = [];
  /** Filters are set but leave nothing to draw. */
  filterEmpty = false;
  private lang = inject(LanguageService);
  private searchDebounce?: ReturnType<typeof setTimeout>;
  /** Everything buildTopology produced; the filter picks the visible part from it. */
  private full: { nodes: GNode[]; links: GLink[] } = { nodes: [], links: [] };
  private parentOf = new Map<string, string>();
  private neighbours = new Map<string, string[]>();
  /** GA addresses the graph placed per location (externalId). */
  private gasByLoc = new Map<string, string[]>();
  private childLocs = new Map<string, LocationDto[]>();
  private locByExt = new Map<string, LocationDto>();
  private nodeSearch = new SearchCache<GNode>((n) => n.kind === 'ga' ? `${n.address} ${n.label}` : n.label);

  private gas: GroupAddressDto[] = [];
  private devices: DeviceDto[] = [];
  private locations: LocationDto[] = [];
  private comObjects: CommunicationObjectDto[] = [];
  private ranges: GroupRangeDto[] = [];
  private gaByAddr = new Map<string, GroupAddressDto>();
  private gasOfDevice = new Map<string, string[]>();

  private graph: any = null;
  private gaNodeByAddr = new Map<string, GNode>();
  private linksByNode = new Map<string, GLink[]>();
  private telegramSub?: Subscription;
  private resizeObs?: ResizeObserver;
  private rafId?: number;
  private skin = {
    write: '#2F7A43', read: '#2563A6', response: '#B26B07',
    ink: '#16201D', ink2: '#51605B', ink3: '#8A958F', line: '#C7CCC4', surface: '#FFFFFF',
  };

  ngOnInit(): void {
    this.readSkin();
    this.loadData();
    this.liveBuffer.start();
    this.telegramSub = this.signalr.telegram$.subscribe((t) => this.onTelegram(t));
  }

  ngAfterViewInit(): void { /* graph created once data + host ready */ }

  ngOnDestroy(): void {
    this.telegramSub?.unsubscribe();
    clearTimeout(this.searchDebounce);
    this.resizeObs?.disconnect();
    if (this.rafId) cancelAnimationFrame(this.rafId);
    this.graph?._destructor?.();
    this.graph = null;
    this.gaNodeByAddr.clear();
    this.linksByNode.clear();
  }

  // --- data ------------------------------------------------------------------
  private loadData(): void {
    this.loading = true;
    this.projectService.getAllProjects().subscribe({
      next: (projects) => {
        const active = projects.find((p) => p.isActive);
        if (!active) { this.hasActiveProject = false; this.loading = false; return; }
        this.hasActiveProject = true;
        forkJoin({
          details: this.projectService.getProjectDetails(active.id),
          locations: this.projectService.getLocations(active.id),
          comObjects: this.projectService.getCommObjects(active.id),
          ranges: this.projectService.getGroupRanges(active.id),
        }).subscribe({
          next: ({ details, locations, comObjects, ranges }) => {
            this.gas = details.groupAddresses ?? [];
            this.devices = details.devices ?? [];
            this.locations = locations ?? [];
            this.comObjects = comObjects ?? [];
            this.ranges = (ranges ?? []).slice().sort((a, b) => (a.rangeEnd - a.rangeStart) - (b.rangeEnd - b.rangeStart));
            this.gaByAddr = new Map(this.gas.map((g) => [g.address, g]));
            this.buildDeviceGaMap();
            this.loading = false;
            this.ensureGraphAndRender();
          },
          error: () => { this.loading = false; },
        });
      },
      error: () => { this.hasActiveProject = false; this.loading = false; },
    });
  }

  private buildDeviceGaMap(): void {
    this.gasOfDevice.clear();
    for (const co of this.comObjects) {
      if (!this.gaByAddr.has(co.groupAddressLink)) continue;
      const arr = this.gasOfDevice.get(co.deviceAddress) ?? [];
      if (!arr.includes(co.groupAddressLink)) arr.push(co.groupAddressLink);
      this.gasOfDevice.set(co.deviceAddress, arr);
    }
  }

  // --- graph -----------------------------------------------------------------
  private ensureGraphAndRender(): void {
    if (!this.hostRef) return;
    if (!this.graph) this.initGraph();
    this.full = this.buildTopology();

    // Links always point from the parent hub to the child, so the target's parent is the source.
    this.parentOf.clear();
    this.neighbours.clear();
    for (const l of this.full.links) {
      const s = this.endId(l.source);
      const t = this.endId(l.target);
      this.parentOf.set(t, s);
      (this.neighbours.get(s) ?? this.neighbours.set(s, []).get(s)!).push(t);
      (this.neighbours.get(t) ?? this.neighbours.set(t, []).get(t)!).push(s);
    }
    this.buildFilterOptions();
    this.applyFilter();
  }

  private endId(end: string | GNode): string {
    return typeof end === 'string' ? end : end.id;
  }

  /** Hands the filtered part of the full graph to force-graph. Node objects are shared, so
   *  positions survive a filter change and the layout does not start over. */
  private applyFilter(): void {
    if (!this.graph) return;
    const keep = this.visibleIds();
    const nodes = keep ? this.full.nodes.filter((n) => keep.has(n.id)) : this.full.nodes;
    const links = keep
      ? this.full.links.filter((l) => keep.has(this.endId(l.source)) && keep.has(this.endId(l.target)))
      : this.full.links;

    this.nodeCount = nodes.length;
    this.linkCount = links.length;
    this.filterEmpty = !!keep && nodes.length === 0;
    // Only drawn links may carry live particles.
    this.linksByNode.clear();
    for (const l of links) {
      const s = this.endId(l.source);
      const t = this.endId(l.target);
      (this.linksByNode.get(s) ?? this.linksByNode.set(s, []).get(s)!).push(l);
      (this.linksByNode.get(t) ?? this.linksByNode.set(t, []).get(t)!).push(l);
    }
    this.pinFloors(nodes);
    this.graph.graphData({ nodes, links });
    this.graph.d3ReheatSimulation();
    setTimeout(() => this.graph?.zoomToFit(600, 40), 1400);
  }

  /** Ids of the nodes to draw, or null when no filter is set and everything shows. */
  private visibleIds(): Set<string> | null {
    const f = this.filter;
    const tokens = searchTokens(f.q);
    if (!f.group && !f.room && !f.dpt && tokens.length === 0) return null;

    const roomGas = f.room ? this.gasOfLocation(f.room) : null;
    const allowed = (n: GNode): boolean => {
      if (n.kind !== 'ga' || !n.address) return true;
      const addr = n.address;
      if (f.group && addr !== f.group && !addr.startsWith(`${f.group}/`)) return false;
      if (f.dpt && this.dptMain(this.gaByAddr.get(addr)?.datapointType) !== f.dpt) return false;
      if (roomGas && !roomGas.has(addr)) return false;
      return true;
    };

    const keep = new Set<string>();
    if (tokens.length) {
      // Hits, then breadth-first along the drawn links. Hubs may be stepped through, GAs only
      // when the other filters allow them.
      const byId = new Map(this.full.nodes.map((n) => [n.id, n]));
      let frontier = this.full.nodes.filter((n) => allowed(n) && this.nodeSearch.matches(n, tokens)).map((n) => n.id);
      frontier.forEach((id) => keep.add(id));
      for (let level = 0; level < f.depth && frontier.length; level++) {
        const next: string[] = [];
        for (const id of frontier) {
          for (const nb of this.neighbours.get(id) ?? []) {
            const node = byId.get(nb);
            if (keep.has(nb) || !node || !allowed(node)) continue;
            keep.add(nb);
            next.push(nb);
          }
        }
        frontier = next;
      }
    } else {
      for (const n of this.full.nodes) if (n.kind === 'ga' && allowed(n)) keep.add(n.id);
    }

    // The hub chain above every visible node, so a GA never floats without its room.
    for (const id of [...keep]) {
      let p = this.parentOf.get(id);
      let guard = 0;
      while (p && !keep.has(p) && guard++ < 50) { keep.add(p); p = this.parentOf.get(p); }
    }
    return keep;
  }

  private dptMain(dpt?: string): string {
    return (dpt ?? '').match(/\d+/)?.[0] ?? '';
  }

  /**
   * GAs belonging to a location and everything below it: the ones the graph placed there plus
   * the ones linked to devices mounted there. The graph has no device nodes, so the devices
   * themselves only show through their GAs.
   */
  private gasOfLocation(extId: string): Set<string> {
    const out = new Set<string>();
    const stack = [extId];
    const seen = new Set<string>();
    while (stack.length) {
      const id = stack.pop()!;
      if (seen.has(id)) continue;
      seen.add(id);
      for (const a of this.gasByLoc.get(id) ?? []) out.add(a);
      const loc = this.locByExt.get(id);
      for (const a of loc?.groupAddresses ?? []) if (this.gaByAddr.has(a)) out.add(a);
      for (const d of loc?.deviceAddresses ?? []) for (const a of this.gasOfDevice.get(d) ?? []) out.add(a);
      for (const c of this.childLocs.get(id) ?? []) stack.push(c.externalId);
    }
    return out;
  }

  /** Select options from the loaded project; a stored filter value that no longer exists
   *  (other project, re-import) is dropped instead of leaving an empty graph. */
  private buildFilterOptions(): void {
    // Main and middle groups present in the project, named after their group range.
    const mains = new Set<number>();
    const middles = new Set<string>();
    for (const ga of this.gas) {
      const p = ga.address.split('/').map(Number);
      if (!Number.isFinite(p[0])) continue;
      mains.add(p[0]);
      if (p.length === 3 && Number.isFinite(p[1])) middles.add(`${p[0]}/${p[1]}`);
    }
    const named = (value: string, name: string | null) => name ? `${value}  ${name}` : value;
    this.groupOptions = [];
    for (const m of [...mains].sort((a, b) => a - b)) {
      this.groupOptions.push({ value: String(m), label: named(`${m}/x`, this.groupName(m * 2048, 2047, true)), depth: 0 });
      const subs = [...middles].filter((k) => k.startsWith(`${m}/`))
        .sort((a, b) => Number(a.split('/')[1]) - Number(b.split('/')[1]));
      for (const k of subs) {
        const mid = Number(k.split('/')[1]);
        this.groupOptions.push({ value: k, label: named(`${k}/x`, this.groupName(m * 2048 + mid * 256, 255, false)), depth: 1 });
      }
    }

    // Location tree in project order, indented; only places that lead to at least one GA.
    this.childLocs.clear();
    this.locByExt = new Map(this.locations.map((l) => [l.externalId, l]));
    const byExt = this.locByExt;
    const roots: LocationDto[] = [];
    for (const l of this.locations) {
      if (l.parentExternalId && byExt.has(l.parentExternalId)) {
        (this.childLocs.get(l.parentExternalId) ?? this.childLocs.set(l.parentExternalId, []).get(l.parentExternalId)!).push(l);
      } else {
        roots.push(l);
      }
    }
    this.roomOptions = [];
    const walk = (list: LocationDto[], depth: number, guard: Set<string>) => {
      for (const l of [...list].sort((a, b) => a.name.localeCompare(b.name, undefined, { numeric: true }))) {
        if (guard.has(l.externalId)) continue;
        guard.add(l.externalId);
        if (this.gasOfLocation(l.externalId).size === 0) continue;
        this.roomOptions.push({ value: l.externalId, label: l.name, depth });
        walk(this.childLocs.get(l.externalId) ?? [], depth + 1, guard);
      }
    };
    walk(roots, 0, new Set());

    const dpts = new Set<string>();
    for (const ga of this.gas) {
      const m = this.dptMain(ga.datapointType);
      if (m) dpts.add(m);
    }
    this.dptOptions = [...dpts].sort((a, b) => Number(a) - Number(b));

    const f = this.filter;
    if (f.group && !this.groupOptions.some((o) => o.value === f.group)) f.group = '';
    if (f.room && !this.roomOptions.some((o) => o.value === f.room)) f.room = '';
    if (f.dpt && !this.dptOptions.includes(f.dpt)) f.dpt = '';
  }

  /** Name of the group range covering a main (span >= 256) or middle (span < 256) block. */
  private groupName(start: number, size: number, main: boolean): string | null {
    const r = this.ranges.find((x) =>
      x.rangeStart <= start + size && x.rangeEnd >= start && x.name &&
      (main ? (x.rangeEnd - x.rangeStart) >= 256 : (x.rangeEnd - x.rangeStart) < 256));
    return r?.name ?? null;
  }

  /** Option text with a leading indent for the nesting level (native selects ignore padding). */
  optionLabel(o: FilterOption): string {
    return '   '.repeat(o.depth) + o.label;
  }

  dptLabel(main: string): string {
    const d = DPT_DESCRIPTIONS[main];
    const text = d ? (this.lang.lang() === 'de' ? d.de : d.en) : '';
    return text ? `DPT ${main}  ${text}` : `DPT ${main}`;
  }

  get hasActiveFilter(): boolean {
    const f = this.filter;
    return !!(f.group || f.room || f.dpt || f.q.trim());
  }

  onFilterChange(): void {
    clearTimeout(this.searchDebounce);
    this.saveFilterPref();
    this.applyFilter();
  }

  /** Typing re-filters after a pause; every keystroke would restart the layout. */
  onSearchInput(): void {
    clearTimeout(this.searchDebounce);
    this.searchDebounce = setTimeout(() => this.onFilterChange(), 250);
  }

  setDepth(d: number): void {
    if (this.filter.depth === d) return;
    this.filter = { ...this.filter, depth: d };
    // Without a search term the depth has no effect; just remember it.
    if (this.filter.q.trim()) this.onFilterChange(); else this.saveFilterPref();
  }

  clearSearch(): void {
    this.filter = { ...this.filter, q: '' };
    this.onFilterChange();
  }

  resetFilter(): void {
    this.filter = { ...EMPTY_FILTER, depth: this.filter.depth };
    this.onFilterChange();
  }

  private static readFilterPref(): GraphFilter {
    try {
      const raw = localStorage.getItem(FILTER_KEY);
      if (raw) {
        const p = JSON.parse(raw) as Partial<GraphFilter>;
        const str = (v: unknown) => typeof v === 'string' ? v : '';
        const depth = Number(p.depth);
        return {
          group: str(p.group), room: str(p.room), dpt: str(p.dpt), q: str(p.q),
          depth: depth >= 1 && depth <= 3 ? Math.round(depth) : 1,
        };
      }
    } catch { /* Kaputter oder gesperrter Speicher: ungefiltert starten. */ }
    return { ...EMPTY_FILTER };
  }

  private saveFilterPref(): void {
    try {
      localStorage.setItem(FILTER_KEY, JSON.stringify(this.filter));
    } catch { /* Nicht merken zu können ist kein Fehler, den der Nutzer sehen muss. */ }
  }

  /**
   * Anchor the structural backbone: building at the centre, each floor pinned to its
   * own evenly-spaced angular sector. Rooms + GAs stay UNpinned → they bloom freely
   * (organic force) around their pinned floor, so storeys separate without crossings.
   */
  private pinFloors(nodes: GNode[]): void {
    for (const n of nodes) { n.fx = undefined; n.fy = undefined; }   // all free…
    const floors = nodes.filter((n) => n.hub === 'floor');
    floors.sort((a, b) => a.label.localeCompare(b.label));
    const R = Math.max(190, floors.length * 62);
    floors.forEach((f, i) => {                                        // …except floors: fixed sectors
      const ang = (i / Math.max(1, floors.length)) * 2 * Math.PI - Math.PI / 2;
      f.fx = Math.cos(ang) * R;
      f.fy = Math.sin(ang) * R;
    });
  }

  private initGraph(): void {
    const el = this.hostRef.nativeElement;
    this.zone.runOutsideAngular(() => {
      const g: any = new ForceGraph(el);
      g.backgroundColor('rgba(0,0,0,0)')
        .nodeId('id')
        .nodeRelSize(4)
        .nodeVal((n: GNode) => n.val)
        // force-graph renders the label as HTML; names come from the ETS project.
        .nodeLabel((n: GNode) => escapeHtml(n.kind === 'ga' ? `${n.address} · ${n.label}` : n.label))
        .linkColor((l: GLink) => this.withAlpha(this.skin.line, l.lvl === 'rg' ? 0.32 : 0.6))
        .linkWidth((l: GLink) => l.lvl === 'bf' ? 1.4 : l.lvl === 'fr' ? 1.0 : 0.5)
        .linkCurvature(0.12)
        .linkDirectionalParticleWidth(2.6)
        .linkDirectionalParticleSpeed(0.02)
        .linkDirectionalParticleColor((l: GLink) => l.__pc || this.skin.write)
        .warmupTicks(80)
        .cooldownTime(9000)
        .autoPauseRedraw(false)
        .onNodeClick((n: GNode) => this.onNodeClick(n))
        .nodeCanvasObject((n: GNode, ctx: CanvasRenderingContext2D, scale: number) => this.paintNode(n, ctx, scale))
        .nodePointerAreaPaint((n: GNode, color: string, ctx: CanvasRenderingContext2D) => {
          ctx.fillStyle = color;
          ctx.beginPath();
          ctx.arc(n.x!, n.y!, this.radius(n) + 2, 0, 2 * Math.PI);
          ctx.fill();
        });
      // Organic force layout — but building + floors are PINNED to fixed angular
      // sectors (see pinFloors) so storeys never interleave / links don't cross,
      // while rooms + GAs bloom freely around their pinned floor.
      g.d3Force('charge')?.strength((n: GNode) => {
        const h = n.hub;
        if (h === 'building') return -300;
        if (h === 'floor') return -160;
        if (h === 'room') return -130;
        return n.kind === 'ga' ? -22 : -50;
      });
      g.d3Force('link')?.distance((l: GLink) =>
        l.lvl === 'rg' ? 18 : l.lvl === 'fr' ? 46 : l.lvl === 'bf' ? 60 : 24
      ).strength((l: GLink) =>
        l.lvl === 'rg' ? 0.7 : l.lvl === 'fr' ? 0.45 : l.lvl === 'bf' ? 0.5 : 0.3
      );
      g.d3Force('center', null);
      g.d3Force('cluster', this.clusterForce(0.2));
      this.graph = g;
      this.fitSize();
      this.resizeObs = new ResizeObserver(() => this.fitSize());
      this.resizeObs.observe(el);
      this.startAnimLoop();
    });
  }

  /** d3 force nudging each GA toward the centroid of its (room+function) cluster. */
  private clusterForce(strength: number) {
    let nodes: GNode[] = [];
    const force = (alpha: number) => {
      const cen = new Map<string, { x: number; y: number; n: number }>();
      for (const nd of nodes) {
        if (!nd.cluster || nd.x == null) continue;
        const c = cen.get(nd.cluster) || { x: 0, y: 0, n: 0 };
        c.x += nd.x; c.y += nd.y!; c.n++;
        cen.set(nd.cluster, c);
      }
      for (const c of cen.values()) { c.x /= c.n; c.y /= c.n; }
      const k = strength * alpha;
      for (const nd of nodes) {
        if (!nd.cluster || nd.x == null) continue;
        const c = cen.get(nd.cluster)!;
        if (c.n < 2) continue;
        nd.vx = (nd.vx || 0) + (c.x - nd.x) * k;
        nd.vy = (nd.vy || 0) + (c.y - nd.y!) * k;
      }
    };
    (force as unknown as { initialize: (n: GNode[]) => void }).initialize = (n: GNode[]) => { nodes = n; };
    return force;
  }

  private fitSize(): void {
    if (!this.graph || !this.hostRef) return;
    const el = this.hostRef.nativeElement;
    this.graph.width(el.clientWidth).height(el.clientHeight);
  }

  private buildTopology(): { nodes: GNode[]; links: GLink[] } {
    const nodes: GNode[] = [];
    const links: GLink[] = [];
    this.gaNodeByAddr.clear();
    const byExt = new Map(this.locations.map((l) => [l.externalId, l]));
    const hubNodes = new Map<string, GNode>();
    const funcColors = new Map<string, string>();
    const legend: { label: string; color: string }[] = [];

    const hubKind = (type: string): 'building' | 'floor' | 'room' =>
      type === 'Floor' ? 'floor' : (type === 'Building' || type === 'BuildingPart') ? 'building' : 'room';
    const hubVal = (k: string) => k === 'building' ? 20 : k === 'floor' ? 14 : 10;
    const lvlFor = (parentType?: string): GLink['lvl'] =>
      (parentType === 'Building' || parentType === 'BuildingPart') ? 'bf' : 'fr';

    const funcKey = (addr: string) => this.rangeName(this.rawAddr(addr)) || `${addr.split('/')[0]}/x`;
    const colorForKey = (key: string): string => {
      if (!funcColors.has(key)) {
        const c = PALETTE[funcColors.size % PALETTE.length];
        funcColors.set(key, c);
        legend.push({ label: key, color: c });
      }
      return funcColors.get(key)!;
    };

    // floor identity colour (tasteful, few floors) — applied to floor + room hubs
    const floorColors = new Map<string, string>();
    const floorOf = (loc: LocationDto): string => {
      let cur: LocationDto | undefined = loc, guard = 0;
      while (cur && guard++ < 50) {
        if (cur.type === 'Floor') return cur.externalId;
        cur = cur.parentExternalId ? byExt.get(cur.parentExternalId) : undefined;
      }
      return 'none';
    };
    const floorColor = (loc: LocationDto): string => {
      const f = floorOf(loc);
      if (f === 'none') return HUB_GREY.room;
      if (!floorColors.has(f)) floorColors.set(f, FLOOR_PALETTE[floorColors.size % FLOOR_PALETTE.length]);
      return floorColors.get(f)!;
    };

    const ensureHub = (loc: LocationDto): string => {
      const id = `loc:${loc.externalId}`;
      if (hubNodes.has(id)) return id;
      const hk = hubKind(loc.type);
      const color = hk === 'building' ? HUB_GREY.building : floorColor(loc);
      const node: GNode = {
        id, kind: hk === 'building' ? 'group' : 'room', hub: hk,
        label: loc.name, val: hubVal(hk), color,
      };
      hubNodes.set(id, node);
      nodes.push(node);
      const parent = loc.parentExternalId ? byExt.get(loc.parentExternalId) : undefined;
      if (parent) links.push({ source: ensureHub(parent), target: id, lvl: lvlFor(parent.type) });
      return id;
    };

    // device → its location (prefer a real Room over a DistributionBoard/cabinet,
    // so a GA lands where it's USED, not where the actuator sits in the panel)
    const isRoomish = (t: string) => t !== 'DistributionBoard';
    const locOfDevice = new Map<string, LocationDto>();
    for (const loc of this.locations) {
      for (const d of loc.deviceAddresses ?? []) {
        const cur = locOfDevice.get(d);
        if (!cur || (!isRoomish(cur.type) && isRoomish(loc.type))) locOfDevice.set(d, loc);
      }
    }
    // GA → set of devices that link it
    const gaToDevices = new Map<string, string[]>();
    for (const [dev, gaList] of this.gasOfDevice) {
      for (const ga of gaList) {
        const arr = gaToDevices.get(ga) ?? [];
        arr.push(dev);
        gaToDevices.set(ga, arr);
      }
    }

    // Plausibility: a "central" device (logic/visu/server) links far more GAs than a
    // normal sensor/actuator — it must NOT pull every GA into its room. Treat devices
    // whose fan-out is way above the median as central and exclude them from room
    // attribution. Also prefer the SENDER (the button/sensor physically operated).
    const fanout = (d: string) => this.gasOfDevice.get(d)?.length ?? 0;
    const fos = [...this.gasOfDevice.keys()].map(fanout).sort((a, b) => a - b);
    const median = fos.length ? fos[Math.floor(fos.length / 2)] : 0;
    const centralCap = Math.max(12, median * 3);
    const senders = new Set<string>();
    for (const co of this.comObjects) {
      if (co.flags && /send|transmit/i.test(co.flags)) senders.add(`${co.deviceAddress}|${co.groupAddressLink}`);
    }

    // Name-based attribution (STRONGEST): a switched channel belongs to the place it
    // controls, named in the GA ("OG Schlafzimmer …", "Kind1 …"), not the panel it's
    // wired into. Match a room name as a token inside the GA name; disambiguate
    // same-named rooms across floors via the floor abbreviation (OG/EG/UG/DG/KG).
    const norm = (s: string) => (s || '').toLowerCase().replace(/[^a-z0-9äöü]+/g, '');
    const floorAbbrevMap: Record<string, string> = {
      obergeschoss: 'og', erdgeschoss: 'eg', untergeschoss: 'ug', dachgeschoss: 'dg', kellergeschoss: 'kg',
    };
    const floorNameOf = (loc: LocationDto): string => {
      const f = floorOf(loc);
      return f === 'none' ? '' : (byExt.get(f)?.name ?? '');
    };
    const roomIndex = this.locations
      .filter((l) => hubKind(l.type) === 'room')
      .map((l) => {
        const fn = norm(floorNameOf(l));
        return { loc: l, norm: norm(l.name), fab: floorAbbrevMap[fn] ?? fn.slice(0, 2) };
      })
      .filter((r) => r.norm.length >= 3)
      .sort((a, b) => b.norm.length - a.norm.length);   // longest names first (most specific)
    const matchByName = (ga: GroupAddressDto): LocationDto | undefined => {
      const g = norm(ga.name || '');
      if (!g) return undefined;
      let best: LocationDto | undefined; let bestScore = -1;
      for (const r of roomIndex) {
        if (!g.includes(r.norm)) continue;
        const s = r.norm.length + (r.fab && r.fab.length >= 2 && g.includes(r.fab) ? 6 : 0);
        if (s > bestScore) { bestScore = s; best = r.loc; }
      }
      return best;
    };

    // device-location fallback (when the GA name carries no room): specific room
    // sender → specific room → cabinet → central-room (last resort)
    const deviceLoc = (addr: string): LocationDto | undefined => {
      const devs = gaToDevices.get(addr);
      if (!devs) return undefined;
      type Cand = { loc: LocationDto; room: boolean; central: boolean; sender: boolean; fo: number };
      const cands: Cand[] = [];
      for (const d of devs) {
        const l = locOfDevice.get(d);
        if (!l) continue;
        cands.push({ loc: l, room: isRoomish(l.type), central: fanout(d) > centralCap, sender: senders.has(`${d}|${addr}`), fo: fanout(d) });
      }
      if (!cands.length) return undefined;
      const score = (c: Cand) =>
        (c.room && !c.central ? 4000 : 0) +
        (c.room && !c.central && c.sender ? 1000 : 0) +
        (!c.room ? 2000 : 0) +
        (c.room && c.central ? 500 : 0) -
        c.fo;
      return cands.reduce((a, b) => (score(b) > score(a) ? b : a)).loc;
    };

    // assign each GA to exactly ONE best location (name wins, else device location)
    const gasByLoc = new Map<string, string[]>();
    this.gasByLoc = gasByLoc;
    for (const ga of this.gas) {
      const loc = matchByName(ga) ?? deviceLoc(ga.address);
      if (!loc) continue;
      const arr = gasByLoc.get(loc.externalId) ?? [];
      arr.push(ga.address);
      gasByLoc.set(loc.externalId, arr);
    }

    for (const [extId, addrs] of gasByLoc) {
      const loc = byExt.get(extId);
      if (!loc) continue;
      const hubId = ensureHub(loc);
      for (const addr of addrs) {
        const ga = this.gaByAddr.get(addr)!;
        const k = funcKey(addr);
        const node = this.gaNode(ga, colorForKey(k));
        node.cluster = `${hubId}|${k}`;
        nodes.push(node);
        links.push({ source: hubId, target: node.id, lvl: 'rg' });
      }
    }
    this.legend = legend.slice(0, 14);
    return { nodes, links };
  }

  private gaNode(ga: GroupAddressDto, color: string): GNode {
    const node: GNode = { id: `ga:${ga.address}`, label: ga.name || ga.address, kind: 'ga', val: 2.6, color, address: ga.address };
    this.gaNodeByAddr.set(ga.address, node);
    return node;
  }

  private rawAddr(addr: string): number {
    const p = addr.split('/').map(Number);
    if (p.length === 3) return p[0] * 2048 + p[1] * 256 + p[2];
    if (p.length === 2) return p[0] * 2048 + p[1];
    return Number(addr) || 0;
  }
  private rangeName(raw: number): string | null {
    for (const r of this.ranges) if (raw >= r.rangeStart && raw <= r.rangeEnd && r.name) return r.name;
    return null;
  }

  // --- painting --------------------------------------------------------------
  private radius(n: GNode): number {
    if (n.kind === 'ga') return (3.6 + Math.sqrt(n.val)) * (1 + (n.__heat || 0) * 1.3);
    return Math.sqrt(n.val) * 3.1;
  }

  private paintNode(n: GNode, ctx: CanvasRenderingContext2D, scale: number): void {
    const r = this.radius(n);
    // A node that just came back through a filter change has no position until the simulation
    // ticks once; drawing it then throws on non-finite canvas coordinates.
    if (!Number.isFinite(n.x) || !Number.isFinite(n.y) || !Number.isFinite(r)) return;
    const heat = n.__heat || 0;
    if (n.__glow && n.__glow > 0.01) {
      const gr = r + 9 + n.__glow * 16;
      const grad = ctx.createRadialGradient(n.x!, n.y!, r * 0.3, n.x!, n.y!, gr);
      const gc = n.__glowColor || n.color;
      grad.addColorStop(0, this.withAlpha(gc, 0.7 * n.__glow));
      grad.addColorStop(0.5, this.withAlpha(gc, 0.35 * n.__glow));
      grad.addColorStop(1, this.withAlpha(gc, 0));
      ctx.fillStyle = grad;
      ctx.beginPath(); ctx.arc(n.x!, n.y!, gr, 0, 2 * Math.PI); ctx.fill();
    }
    ctx.beginPath(); ctx.arc(n.x!, n.y!, r, 0, 2 * Math.PI);
    ctx.fillStyle = n.kind === 'ga'
      ? this.withAlpha(n.color, 0.32 + 0.68 * Math.min(1, heat + 0.05))
      : this.withAlpha(n.color, 0.95);
    ctx.fill();
    if (n.kind !== 'ga') {
      ctx.lineWidth = 1.4 / scale;
      ctx.strokeStyle = this.withAlpha('#ffffff', 0.65);
      ctx.stroke();
    }
    const showHub = n.kind !== 'ga' && (n.hub === 'room' ? scale > 0.7 : scale > 0.3);
    const live = (n.__glow || 0) > 0.05;
    const showGaName = n.kind === 'ga' && (scale > 1.7 || heat > 0.45);
    if (showHub) {
      const big = n.hub !== 'room';
      this.drawLabel(ctx, n.label, n.x!, n.y! + r + 2, scale, big ? this.skin.ink : this.skin.ink2, big ? 700 : 600, big ? 13 : 11);
    } else if (live && n.__value) {
      this.drawLabel(ctx, n.__value, n.x!, n.y! - r - 2, scale, n.__glowColor || n.color, 700, 12, true);
    } else if (showGaName) {
      this.drawLabel(ctx, n.label, n.x!, n.y! + r + 1.5, scale, this.skin.ink2, 500, 10);
    }
  }

  private drawLabel(ctx: CanvasRenderingContext2D, text: string, x: number, y: number, scale: number, color: string, weight: number, px: number, above = false): void {
    const fs = px / scale;
    ctx.font = `${weight} ${fs}px ui-sans-serif, system-ui, sans-serif`;
    ctx.textAlign = 'center';
    ctx.textBaseline = above ? 'bottom' : 'top';
    ctx.lineWidth = 3 / scale;
    ctx.strokeStyle = this.withAlpha(this.skin.surface, 0.85);
    ctx.strokeText(text, x, y);
    ctx.fillStyle = color;
    ctx.fillText(text, x, y);
  }

  // --- live ------------------------------------------------------------------
  private onTelegram(t: KnxTelegram): void {
    const node = this.gaNodeByAddr.get(t.destinationAddress);
    if (!node) return;
    const kind = messageTypeKind(t.messageType) || 'write';
    const col = kind === 'read' ? this.skin.read : kind === 'response' ? this.skin.response : this.skin.write;
    node.__glow = 1;
    node.__glowColor = col;
    node.__heat = Math.min(1, (node.__heat || 0) + 0.34);
    if (t.valueDecoded) node.__value = t.valueDecoded;
    const links = this.linksByNode.get(node.id);
    if (links && this.graph) for (const l of links) { l.__pc = col; this.graph.emitParticle(l); }
  }

  private startAnimLoop(): void {
    const tick = () => {
      const data = this.graph?.graphData() as { nodes: GNode[] } | undefined;
      if (data) for (const n of data.nodes) {
        if (n.__glow && n.__glow > 0) n.__glow = Math.max(0, n.__glow - 0.007);
        if (n.__heat && n.__heat > 0) n.__heat = Math.max(0, n.__heat - 0.0016);
      }
      this.rafId = requestAnimationFrame(tick);
    };
    this.rafId = requestAnimationFrame(tick);
  }

  private onNodeClick(n: GNode): void {
    if (n?.kind === 'ga' && n.address) {
      this.zone.run(() => this.router.navigate(['/charts'], { queryParams: { ga: n.address } }));
    } else if (this.graph) {
      this.graph.centerAt(n.x, n.y, 500);
      this.graph.zoom(Math.max(2, this.graph.zoom()), 500);
    }
  }

  // --- skin / utils ----------------------------------------------------------
  private readSkin(): void {
    const cs = getComputedStyle(document.documentElement);
    const tok = (n: string, fb: string) => cs.getPropertyValue(n).trim() || fb;
    this.skin = {
      write: tok('--write', '#2F7A43'), read: tok('--read', '#2563A6'), response: tok('--response', '#B26B07'),
      ink: tok('--ink', '#16201D'), ink2: tok('--ink-2', '#51605B'), ink3: tok('--ink-3', '#8A958F'),
      line: tok('--line-strong', '#C7CCC4'), surface: tok('--surface', '#FFFFFF'),
    };
  }

  private withAlpha(color: string, alpha: number): string {
    const m = /^#?([0-9a-f]{6})$/i.exec(color.trim());
    if (!m) return color;
    const n = parseInt(m[1], 16);
    return `rgba(${(n >> 16) & 255},${(n >> 8) & 255},${n & 255},${alpha})`;
  }

  // --- legend ----------------------------------------------------------------
  /** Statisch, damit der Field-Initializer sie nutzen kann — die Wahl steht damit
   *  vor dem ersten Render fest und die Legende flackert beim Laden nicht auf. */
  private static readLegendPref(): boolean {
    try {
      const raw = localStorage.getItem(LEGEND_KEY);
      if (raw === 'on') return true;
      if (raw === 'off') return false;
    } catch { /* Private Mode / Storage aus — Viewport-Default ist immer okay. */ }
    // Ohne `matchMedia` (jsdom, alte WebViews) liefert die Kette `undefined`; `!undefined`
    // ist `true` und damit der gewollte Desktop-Default. Absichtlich, kein Versehen —
    // die Methode läuft aus einem Field-Initializer, ein Wurf hier würde die ganze
    // Komponente nicht konstruieren lassen statt nur den Viewport-Default zu kosten.
    return !window.matchMedia?.('(max-width: 767px)')?.matches;
  }

  /** Ohne Legendeneinträge gibt es nichts zu togglen — Button bleibt dann disabled. */
  get canToggleLegend(): boolean {
    return this.legend.length > 0 && !this.loading && this.hasActiveProject;
  }

  toggleLegend(): void {
    this.showLegend = !this.showLegend;
    try {
      localStorage.setItem(LEGEND_KEY, this.showLegend ? 'on' : 'off');
    } catch { /* Nicht merken zu können ist kein Fehler, den der Nutzer sehen muss. */ }
  }

  zoomToFit(): void { this.graph?.zoomToFit(400, 40); }
}
