import { Component, Inject, inject } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { TranslatePipe } from './../core/i18n/translate.pipe';

export interface ConfirmDialogData {
  title: string;
  message: string;
  warning?: string;       // emphasised line (e.g. "This cannot be undone.")
  confirmText?: string;   // default "Confirm"
  cancelText?: string;    // default "Cancel"
  danger?: boolean;       // red confirm button
}

/**
 * Generic confirmation dialog. Resolves the MatDialog with `true` on confirm,
 * `false`/undefined otherwise.
 */
@Component({
  selector: 'app-confirm-dialog',
  standalone: true,
  imports: [MatIconModule, MatButtonModule, MatDialogModule, TranslatePipe],
  template: `
    <div class="knx-confirm">
      <header class="cd-head" [class.danger]="data.danger">
        <mat-icon>{{ data.danger ? 'warning' : 'help' }}</mat-icon>
        <h3>{{ data.title }}</h3>
      </header>
      <p class="cd-msg">{{ data.message }}</p>
      @if (data.warning) {
        <p class="cd-warn">{{ data.warning }}</p>
      }
      <div class="cd-actions">
        <button class="knx-btn knx-btn--ghost" (click)="close(false)">{{ data.cancelText || ('common.cancel' | translate) }}</button>
        <button class="knx-btn" [class.knx-btn--danger]="data.danger" [class.knx-btn--primary]="!data.danger"
                (click)="close(true)">{{ data.confirmText || ('common.confirm' | translate) }}</button>
      </div>
    </div>
  `,
  styles: [`
    .knx-confirm { padding: var(--sp-5); max-width: 420px; }
    .cd-head { display: flex; align-items: center; gap: var(--sp-2); margin-bottom: var(--sp-3); }
    .cd-head h3 { margin: 0; color: var(--ink); font-weight: var(--fw-semi); }
    .cd-head.danger mat-icon { color: var(--err); }
    .cd-msg { color: var(--ink-2); font-size: var(--fs-md); margin: 0 0 var(--sp-2); }
    .cd-warn { color: var(--err); font-weight: var(--fw-medium); font-size: var(--fs-sm); margin: 0 0 var(--sp-4); }
    .cd-actions { display: flex; justify-content: flex-end; gap: var(--sp-2); margin-top: var(--sp-4); }
  `]
})
export class ConfirmDialogComponent {
  constructor(
    @Inject(MAT_DIALOG_DATA) public data: ConfirmDialogData,
    private ref: MatDialogRef<ConfirmDialogComponent, boolean>
  ) {}

  close(result: boolean): void { this.ref.close(result); }
}
