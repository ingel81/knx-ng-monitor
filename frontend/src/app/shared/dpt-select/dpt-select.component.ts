import { Component, ElementRef, EventEmitter, Input, Output, ViewChild, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatSelectModule } from '@angular/material/select';
import { LanguageService } from '../../core/i18n/language.service';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { DPT_DESCRIPTIONS } from '../grid/dpt-descriptions';
import { parseDpt } from '../grid/knx-grid.util';
import { foldForSearch, searchTokens } from '../search.util';

/** One main type with the subtypes that occur under it, e.g. { main: '9', subs: ['9.001', '9.007'] }. */
export interface DptGroup {
  main: string;
  subs: string[];
}

/** Groups the DPTs found in a list ("DPST-9-7", "9.001", "DPT-1", …) by main type, sorted. */
export function dptGroupsOf(dpts: Iterable<string | null | undefined>): DptGroup[] {
  const subsByMain = new Map<number, Set<number>>();
  for (const dpt of dpts) {
    const p = parseDpt(dpt);
    if (!p) continue;
    let subs = subsByMain.get(p.main);
    if (!subs) { subs = new Set(); subsByMain.set(p.main, subs); }
    if (p.sub !== null) subs.add(p.sub);
  }
  return [...subsByMain.keys()].sort((a, b) => a - b).map(main => ({
    main: String(main),
    subs: [...subsByMain.get(main)!].sort((a, b) => a - b).map(sub => `${main}.${String(sub).padStart(3, '0')}`),
  }));
}

/** Filter value semantics shared by every view: "" = all, "9" = every DPT 9, "9.007" = exactly 9.007. */
export function matchesDptFilter(dpt: string | null | undefined, filter: string): boolean {
  if (!filter) return true;
  const p = parseDpt(dpt);
  if (!p) return false;
  const [main, sub] = filter.split('.').map(Number);
  return p.main === main && (sub === undefined || p.sub === sub);
}

/**
 * DPT filter for filter bars (#45): main types as groups, each with "all of DPT n" and the
 * subtypes that occur, plus a search row. Used by the group address list and the graph, so
 * both offer the same choices in the same look.
 */
@Component({
  selector: 'knx-dpt-select',
  standalone: true,
  imports: [FormsModule, MatFormFieldModule, MatSelectModule, MatIconModule, TranslatePipe],
  template: `
    <mat-form-field appearance="outline" class="knx-select" [class.is-active]="!!value" subscriptSizing="dynamic">
      <mat-select [ngModel]="value" (ngModelChange)="pick($event)" (openedChange)="onOpened($event)"
                  panelClass="knx-select-panel"
                  [attr.aria-label]="'graph.filter.dpt' | translate">
        <mat-select-trigger>{{ value ? optionText(value) : ('graph.filter.dptAll' | translate) }}</mat-select-trigger>

        <div class="knx-select-search" (click)="$event.stopPropagation()">
          <mat-icon>search</mat-icon>
          <input #search class="knx-input" type="text"
                 [(ngModel)]="query" [ngModelOptions]="{ standalone: true }"
                 [placeholder]="'ga.dptSearch' | translate"
                 (keydown)="onSearchKeydown($event)" />
        </div>

        <mat-option value="">{{ 'graph.filter.dptAll' | translate }}</mat-option>
        @for (g of visibleGroups(); track g.main) {
          <mat-optgroup [label]="groupLabel(g.main)">
            <mat-option [value]="g.main">{{ 'ga.dptAllOfMain' | translate: { main: g.main } }}</mat-option>
            @for (sub of g.subs; track sub) {
              <mat-option [value]="sub"><span class="opt-key">{{ sub }}</span>{{ text(sub) }}</mat-option>
            }
          </mat-optgroup>
        }
        @if (visibleGroups().length === 0) {
          <div class="knx-select-empty">{{ 'charts.searchNoMatch' | translate }}</div>
        }
      </mat-select>
    </mat-form-field>
  `,
  styles: [`
    :host { display: inline-block; width: 260px; }
    .knx-select { width: 100%; }
  `],
})
export class DptSelectComponent {
  private lang = inject(LanguageService);

  @Input() value = '';
  @Output() valueChange = new EventEmitter<string>();

  /** The DPT strings of the items to filter; the options are built from what occurs in them. */
  @Input() set dpts(list: Iterable<string | null | undefined>) {
    this.groups = dptGroupsOf(list);
  }

  @ViewChild('search') private searchInput?: ElementRef<HTMLInputElement>;

  groups: DptGroup[] = [];
  query = '';

  pick(v: string): void {
    this.value = v ?? '';
    this.valueChange.emit(this.value);
  }

  onOpened(open: boolean): void {
    if (!open) return;
    this.query = '';
    // The overlay needs a tick to exist before the input can take focus.
    setTimeout(() => this.searchInput?.nativeElement.focus());
  }

  // Typing belongs to the search field; only the list keys reach the select.
  onSearchKeydown(event: KeyboardEvent): void {
    if (event.key !== 'Escape' && event.key !== 'ArrowDown' && event.key !== 'ArrowUp') {
      event.stopPropagation();
    }
  }

  /**
   * Groups matching the search, word by word, against number and name. A matching main type
   * keeps all its subtypes. The selected entry always stays rendered so the select keeps it.
   */
  visibleGroups(): DptGroup[] {
    const tokens = searchTokens(this.query);
    if (tokens.length === 0) return this.groups;
    const hit = (key: string) => {
      const hay = foldForSearch(`${key} ${this.text(key)}`);
      return tokens.every(t => hay.includes(t));
    };
    const result: DptGroup[] = [];
    for (const g of this.groups) {
      if (hit(`dpt ${g.main}`) || g.main === this.value) { result.push(g); continue; }
      const subs = g.subs.filter(s => hit(s) || s === this.value);
      if (subs.length) result.push({ main: g.main, subs });
    }
    return result;
  }

  /** Description of a DPT key ("9" or "9.007") in the UI language. */
  text(key: string): string {
    const d = DPT_DESCRIPTIONS[key];
    return d ? (this.lang.lang() === 'de' ? d.de : d.en) : '';
  }

  groupLabel(main: string): string {
    const t = this.text(main);
    return t ? `DPT ${main} · ${t}` : `DPT ${main}`;
  }

  /** What the closed field shows for the current value. */
  optionText(key: string): string {
    if (!key.includes('.')) return this.lang.translate('ga.dptAllOfMain', { main: key });
    const t = this.text(key);
    return t ? `${key} ${t}` : key;
  }
}
