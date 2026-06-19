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

  private buildTree(flat: LocationDto[]): LocationNode[] {
    const byId = new Map<string, LocationNode>();
    for (const loc of flat) {
      byId.set(loc.externalId, { ...loc, children: [] });
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
    return roots;
  }

  setAllOpen(open: boolean): void {
    this.allOpen = open;
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
