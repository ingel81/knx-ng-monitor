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
 *
 * Zusätzlich wird unter `<storageKey>.seen` festgehalten, welche Spalten dem Nutzer schon einmal
 * angeboten wurden. Nur so lässt sich eine später hinzugefügte Spalte verborgen ausliefern, ohne
 * die bestehende Auswahl zurückzusetzen.
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
    let stored: string[] | null;
    try { stored = raw ? JSON.parse(raw) : null; } catch { stored = null; }

    if (stored === null) {
      this.hidden = new Set(this.defaultHidden);
    } else {
      this.hidden = new Set(stored);
      // Spalten, die es beim letzten Speichern noch nicht gab, starten auf ihrem Default statt
      // sichtbar zu sein: sonst taucht jede neu ausgelieferte Spalte bei Bestandsnutzern
      // ungefragt im Grid auf, weil sie im gespeicherten Hidden-Set naturgemäß fehlt.
      const seen = this.readSeen();
      for (const c of this.columns) {
        if (!seen.includes(c.key) && this.defaultHidden.includes(c.key)) this.hidden.add(c.key);
      }
    }

    this.locked.forEach((k) => this.hidden.delete(k));
    // Beides zusammen persistieren: würde nur `seen` geschrieben, gälte die neue Spalte beim
    // nächsten Laden als bekannt, stünde aber weiterhin nicht im gespeicherten Hidden-Set —
    // und wäre damit ab dem zweiten Start doch sichtbar.
    this.persist();
    this.emit();
  }

  private persist(): void {
    localStorage.setItem(this.storageKey, JSON.stringify([...this.hidden]));
    localStorage.setItem(this.seenKey, JSON.stringify(this.columns.map((c) => c.key)));
  }

  private readSeen(): string[] {
    try { return JSON.parse(localStorage.getItem(this.seenKey) ?? '[]'); } catch { return []; }
  }

  private get seenKey(): string { return `${this.storageKey}.seen`; }

  toggle(key: string, checked: boolean): void {
    if (checked) this.hidden.delete(key); else this.hidden.add(key);
    this.persist();
    this.emit();
  }

  private emit(): void { this.hiddenChange.emit(new Set(this.hidden)); }
}
