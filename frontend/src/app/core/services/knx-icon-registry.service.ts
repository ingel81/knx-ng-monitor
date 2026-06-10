import { Injectable, inject } from '@angular/core';
import { MatIconRegistry } from '@angular/material/icon';
import { DomSanitizer } from '@angular/platform-browser';

/**
 * Eigenes „Instrument"-Icon-Set (stroke-basiert) aus dem Design-Handoff,
 * registriert im Namespace `knx`. Verwendung: <mat-icon svgIcon="knx:live">.
 * Quelle: docs/ai/design_handoff_knx_monitor/design-source/icons.jsx
 */
const KNX_ICONS: Record<string, string[]> = {
  live: ['M2 12s3.5-7 10-7 10 7 10 7-3.5 7-10 7-10-7-10-7z', 'circle:12,12,3'],
  history: ['M3 3v5h5', 'M3.05 13A9 9 0 1 0 6 5.3L3 8'],
  folder: ['M3 7a2 2 0 0 1 2-2h4l2 2h8a2 2 0 0 1 2 2v8a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z'],
  settings: ['circle:12,12,3', 'M19.4 15a1.65 1.65 0 0 0 .33 1.82l.06.06a2 2 0 1 1-2.83 2.83l-.06-.06a1.65 1.65 0 0 0-2.82 1.17V21a2 2 0 0 1-4 0v-.09A1.65 1.65 0 0 0 8 19.4a1.65 1.65 0 0 0-1.82.33l-.06.06a2 2 0 1 1-2.83-2.83l.06-.06A1.65 1.65 0 0 0 4.6 15H4.5a2 2 0 0 1 0-4h.09A1.65 1.65 0 0 0 6 9.4l-.06-.06a2 2 0 1 1 2.83-2.83l.06.06A1.65 1.65 0 0 0 11 4.6V4.5a2 2 0 0 1 4 0v.09a1.65 1.65 0 0 0 2.82 1.17l.06-.06a2 2 0 1 1 2.83 2.83l-.06.06A1.65 1.65 0 0 0 19.4 11h.1a2 2 0 0 1 0 4h-.1z'],
  search: ['circle:11,11,7', 'M21 21l-4-4'],
  filter: ['M3 4h18l-7 8v6l-4 2v-8z'],
  download: ['M12 3v12M7 11l5 5 5-5M5 21h14'],
  pause: ['M7 5v14M17 5v14'],
  play: ['M7 4l13 8-13 8z'],
  clear: ['M3 6h18M8 6V4h8v2M19 6l-1 14H6L5 6'],
  columns: ['M3 5h18v14H3zM9 5v14M15 5v14'],
  chevron: ['M9 18l6-6-6-6'],
  chevronDown: ['M6 9l6 6 6-6'],
  close: ['M18 6L6 18M6 6l12 12'],
  plus: ['M12 5v14M5 12h14'],
  disconnect: ['M9 12l-3 3a3 3 0 0 1-4-4l3-3M15 12l3-3a3 3 0 0 0-4-4l-3 3M4 20L20 4'],
  link: ['M10 13a5 5 0 0 0 7 0l3-3a5 5 0 0 0-7-7l-1 1', 'M14 11a5 5 0 0 0-7 0l-3 3a5 5 0 0 0 7 7l1-1'],
  user: ['circle:12,8,4', 'M4 21a8 8 0 0 1 16 0'],
  logout: ['M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4M16 17l5-5-5-5M21 12H9'],
  clock: ['circle:12,12,9', 'M12 7v5l3 2'],
  arrowUp: ['M12 19V5M5 12l7-7 7 7'],
  arrowDown: ['M12 5v14M5 12l7 7 7-7'],
  sliders: ['M4 21v-7M4 10V3M12 21v-9M12 8V3M20 21v-5M20 12V3M1 14h6M9 8h6M17 16h6'],
  check: ['M20 6L9 17l-5-5'],
  eye: ['M2 12s3.5-7 10-7 10 7 10 7-3.5 7-10 7-10-7-10-7z', 'circle:12,12,3'],
  trash: ['M3 6h18M8 6V4h8v2M19 6l-1 14H6L5 6M10 11v6M14 11v6'],
  upload: ['M12 16V4M7 9l5-5 5 5M5 20h14'],
  globe: ['circle:12,12,9', 'M3 12h18M12 3a14 14 0 0 1 0 18 14 14 0 0 1 0-18'],
  plug: ['M9 7V3M15 7V3M6 7h12v4a6 6 0 0 1-12 0zM12 17v4'],
  save: ['M5 4h11l3 3v13H5zM8 4v5h7V4M8 20v-6h8v6'],
  cpu: ['M6 6h12v12H6zM9 9h6v6H9M2 9h2M2 14h2M20 9h2M20 14h2M9 2v2M14 2v2M9 20v2M14 20v2'],
  calendar: ['M5 5h14v15H5zM5 9h14M9 3v4M15 3v4'],
  refresh: ['M3 3v5h5', 'M3.05 13A9 9 0 1 0 6 5.3L3 8'],
  wifi: ['M5 12a10 10 0 0 1 14 0M8.5 15.5a5 5 0 0 1 7 0', 'circle:12,19,0.5'],
  sitemap: ['M9 3h6v4H9zM3 17h6v4H3zM15 17h6v4h-6zM12 7v4M6 17v-3h12v3'],
  monitor: ['M3 4h18v12H3zM9 20h6M12 16v4'],
  swap: ['M7 4v13M4 14l3 3 3-3M17 20V7M14 10l3-3 3 3'],
  sun: ['circle:12,12,4', 'M12 2v2M12 20v2M4.9 4.9l1.4 1.4M17.7 17.7l1.4 1.4M2 12h2M20 12h2M4.9 19.1l1.4-1.4M17.7 6.3l1.4-1.4'],
  moon: ['M21 12.8A9 9 0 1 1 11.2 3a7 7 0 0 0 9.8 9.8z'],
  database: ['M12 3c4.4 0 8 1.3 8 3s-3.6 3-8 3-8-1.3-8-3 3.6-3 8-3z', 'M4 6v6c0 1.7 3.6 3 8 3s8-1.3 8-3V6M4 12v6c0 1.7 3.6 3 8 3s8-1.3 8-3v-6'],
};

@Injectable({ providedIn: 'root' })
export class KnxIconRegistry {
  private registry = inject(MatIconRegistry);
  private sanitizer = inject(DomSanitizer);

  register(): void {
    for (const [name, segs] of Object.entries(KNX_ICONS)) {
      this.registry.addSvgIconLiteralInNamespace(
        'knx', name, this.sanitizer.bypassSecurityTrustHtml(this.toSvg(segs)));
    }
  }

  private toSvg(segs: string[]): string {
    const inner = segs.map((s) => {
      if (s.startsWith('circle:')) {
        const [cx, cy, r] = s.slice(7).split(',');
        return `<circle cx="${cx}" cy="${cy}" r="${r}" />`;
      }
      return `<path d="${s}" />`;
    }).join('');
    return `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.75" ` +
      `stroke-linecap="round" stroke-linejoin="round">${inner}</svg>`;
  }
}
