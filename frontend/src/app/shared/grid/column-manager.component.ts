import { Component, EventEmitter, Input, OnInit, Output } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslatePipe } from '../../core/i18n/translate.pipe';

export interface ColumnOption { key: string; header: string; }

/**
 * Spalten-Manager-Popover: blendet Spalten ein/aus (gesperrte bleiben sichtbar).
 * Persistiert die verborgenen Keys pro Grid in localStorage. Emittiert das
 * aktuelle Hidden-Set; der Parent filtert seine Spaltenliste danach.
 */
@Component({
  selector: 'app-column-manager',
  standalone: true,
  imports: [CommonModule, MatButtonModule, MatIconModule, MatMenuModule, MatCheckboxModule, MatTooltipModule, TranslatePipe],
  template: `
    <button class="knx-btn knx-btn--ghost" [matMenuTriggerFor]="menu" [matTooltip]="'columns.toggle' | translate">
      <mat-icon svgIcon="knx:columns"></mat-icon>
      {{ 'columns.label' | translate }}
    </button>
    <mat-menu #menu="matMenu">
      @for (c of columns; track c.key) {
        <div class="col-item" (click)="$event.stopPropagation()">
          <mat-checkbox [checked]="!hidden.has(c.key)" [disabled]="locked.includes(c.key)"
                        (change)="toggle(c.key, $event.checked)">
            {{ c.header | translate }}
          </mat-checkbox>
        </div>
      }
    </mat-menu>
  `,
  styles: [`.col-item { padding: 2px 12px; } .col-item mat-checkbox { font-size: var(--fs-md); }`]
})
export class ColumnManagerComponent implements OnInit {
  @Input() columns: ColumnOption[] = [];
  @Input() storageKey = 'knx.cols';
  @Input() locked: string[] = [];
  @Input() defaultHidden: string[] = []; // initial verborgen, falls nichts persistiert ist
  @Output() hiddenChange = new EventEmitter<Set<string>>();

  hidden = new Set<string>();

  ngOnInit(): void {
    const raw = localStorage.getItem(this.storageKey);
    let stored: string[];
    try { stored = raw ? JSON.parse(raw) : this.defaultHidden; } catch { stored = this.defaultHidden; }
    this.hidden = new Set(stored.filter((k) => !this.locked.includes(k)));
    this.emit();
  }

  toggle(key: string, checked: boolean): void {
    if (checked) this.hidden.delete(key); else this.hidden.add(key);
    localStorage.setItem(this.storageKey, JSON.stringify([...this.hidden]));
    this.emit();
  }

  private emit(): void { this.hiddenChange.emit(new Set(this.hidden)); }
}
