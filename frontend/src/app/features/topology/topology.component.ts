import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatIconModule } from '@angular/material/icon';
import { ProjectService, LocationDto } from '../../core/services/project.service';
import { LanguageService } from '../../core/i18n/language.service';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { LoggerService } from '../../core/logging/logger.service';

/** A location node with its resolved children (rebuilt from the flat DTO list). */
interface LocationNode extends LocationDto {
  children: LocationNode[];
  /**
   * Expanded state, owned by the model rather than the DOM. Binding `[open]` alone is one-way:
   * a manual click changes the element without Angular noticing, so pressing "collapse all"
   * afterwards wrote the unchanged value and left those nodes open.
   */
  open: boolean;
}

/**
 * Building-tree view. Loads the active project's flat location list and rebuilds
 * the nested tree client-side via externalId / parentExternalId. Roots are nodes
 * whose parent is empty or points to an unknown id.
 */
@Component({
  selector: 'app-topology',
  standalone: true,
  imports: [CommonModule, MatIconModule, TranslatePipe],
  templateUrl: './topology.component.html',
  styleUrl: './topology.component.scss'
})
export class TopologyComponent implements OnInit {
  private projectService = inject(ProjectService);
  private lang = inject(LanguageService);
  private logger = inject(LoggerService);

  loading = false;
  error = false;
  hasActiveProject = false;
  roots: LocationNode[] = [];

  /** Maps a device physical address (e.g. "0.0.61") to its resolved name. */
  private deviceNames = new Map<string, string>();

  /** Drives the open/closed state of every <details> via a single bound flag. */
  allOpen = true;

  ngOnInit(): void {
    this.load();
  }

  async load(): Promise<void> {
    this.loading = true;
    this.error = false;
    try {
      const projects = await this.projectService.getAllProjects().toPromise() || [];
      const active = projects.find(p => p.isActive);
      this.hasActiveProject = !!active;
      if (!active) {
        this.roots = [];
        return;
      }
      const details = await this.projectService.getProjectDetails(active.id).toPromise();
      this.deviceNames = new Map(
        (details?.devices ?? [])
          .filter(d => d.physicalAddress && d.name)
          .map(d => [d.physicalAddress, d.name] as [string, string])
      );
      const locations = await this.projectService.getLocations(active.id).toPromise() || [];
      this.roots = this.buildTree(locations);
    } catch (err) {
      this.logger.error('Failed to load topology:', err);
      this.error = true;
      this.roots = [];
    } finally {
      this.loading = false;
    }
  }

  /** Digit-aware so "2_Bad" sorts before "10_Flur" instead of after it. */
  private static readonly collator = new Intl.Collator(undefined, { numeric: true, sensitivity: 'base' });

  /**
   * Compares dotted / slashed KNX addresses segment by segment, so 1.0.9 comes before
   * 1.0.50 — a plain string compare would get that backwards. Anything non-numeric
   * (free group-address style, malformed values) falls back to the collator.
   */
  private static compareAddress(a: string, b: string): number {
    const pa = a.split(/[./]/);
    const pb = b.split(/[./]/);
    for (let i = 0; i < Math.min(pa.length, pb.length); i++) {
      const na = Number(pa[i]);
      const nb = Number(pb[i]);
      if (!Number.isInteger(na) || !Number.isInteger(nb)) {
        return TopologyComponent.collator.compare(a, b);
      }
      if (na !== nb) return na - nb;
    }
    return pa.length - pb.length;
  }

  private buildTree(flat: LocationDto[]): LocationNode[] {
    const byId = new Map<string, LocationNode>();
    for (const loc of flat) {
      // Copy the reference arrays before sorting — the DTOs come straight from the
      // service and must not be reordered underneath other consumers.
      byId.set(loc.externalId, {
        ...loc,
        deviceAddresses: [...loc.deviceAddresses].sort(TopologyComponent.compareAddress),
        groupAddresses: [...loc.groupAddresses].sort(TopologyComponent.compareAddress),
        children: [],
        open: this.allOpen
      });
    }
    const roots: LocationNode[] = [];
    for (const node of byId.values()) {
      const parentId = node.parentExternalId;
      const parent = parentId ? byId.get(parentId) : undefined;
      if (parent) {
        parent.children.push(node);
      } else {
        roots.push(node);
      }
    }

    // Without this the tree keeps the order the locations were imported in (ETS document
    // order), which is invisible to the user and reads as arbitrary — floors showed up as
    // UG, OG, EG. Sort every sibling level by name instead.
    const sortLevel = (nodes: LocationNode[]): void => {
      nodes.sort((a, b) => TopologyComponent.collator.compare(a.name, b.name));
      for (const child of nodes) sortLevel(child.children);
    };
    sortLevel(roots);

    return roots;
  }

  setAllOpen(open: boolean): void {
    this.allOpen = open;
    const walk = (nodes: LocationNode[]): void => {
      for (const node of nodes) {
        node.open = open;
        walk(node.children);
      }
    };
    walk(this.roots);
  }

  /** Keeps the model in step with a manual click on the disclosure triangle. */
  onToggle(node: LocationNode, event: Event): void {
    node.open = (event.target as HTMLDetailsElement).open;
  }

  get hasLocations(): boolean {
    return this.roots.length > 0;
  }

  /** Renders a device as "Name (address)", or just the address when no name resolves. */
  deviceLabel(address: string): string {
    const name = this.deviceNames.get(address);
    return name ? `${name} (${address})` : address;
  }
}
