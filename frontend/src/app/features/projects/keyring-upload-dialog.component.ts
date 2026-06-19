import { Component, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatIconModule } from '@angular/material/icon';
import { MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { TranslatePipe } from '../../core/i18n/translate.pipe';

/** Result handed back to the caller, or `null` when the dialog is cancelled. */
export interface KeyringUploadResult {
  file: File;
  password: string;
}

/**
 * Small dialog to pick a `.knxkeys` file and enter its keyring password.
 * Resolves with {@link KeyringUploadResult} on submit, `null`/undefined on cancel.
 */
@Component({
  selector: 'app-keyring-upload-dialog',
  standalone: true,
  imports: [CommonModule, FormsModule, MatIconModule, MatDialogModule, TranslatePipe],
  template: `
    <div class="knx-keyring">
      <header class="kr-head">
        <mat-icon svgIcon="knx:upload"></mat-icon>
        <h3>{{ 'projects.keyringDialogTitle' | translate }}</h3>
      </header>
      <p class="kr-hint">{{ 'projects.keyringDialogHint' | translate }}</p>

      <label class="knx-btn knx-btn--outline kr-file">
        <mat-icon svgIcon="knx:folder"></mat-icon>
        <span>{{ file ? file.name : ('projects.keyringChooseFile' | translate) }}</span>
        <input type="file" accept=".knxkeys" hidden (change)="onFile($event)">
      </label>

      <label class="kr-field">
        <span>{{ 'projects.keyringPassword' | translate }}</span>
        <input class="knx-input" type="password" [(ngModel)]="password" autocomplete="off">
      </label>

      <div class="kr-actions">
        <button class="knx-btn knx-btn--ghost" (click)="close()">{{ 'common.cancel' | translate }}</button>
        <button class="knx-btn knx-btn--primary" [disabled]="!file || !password" (click)="submit()">
          {{ 'projects.keyringUpload' | translate }}
        </button>
      </div>
    </div>
  `,
  styles: [`
    .knx-keyring { padding: var(--sp-5); max-width: 440px; display: flex; flex-direction: column; gap: var(--sp-3); }
    .kr-head { display: flex; align-items: center; gap: var(--sp-2); }
    .kr-head h3 { margin: 0; color: var(--ink); font-weight: var(--fw-semi); }
    .kr-hint { margin: 0; color: var(--ink-2); font-size: var(--fs-sm); }
    .kr-file { justify-content: flex-start; gap: var(--sp-2); cursor: pointer; }
    .kr-file span { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .kr-field { display: flex; flex-direction: column; gap: 3px; font-size: var(--fs-xs); color: var(--ink-2); }
    .kr-field .knx-input { height: 34px; }
    .kr-actions { display: flex; justify-content: flex-end; gap: var(--sp-2); margin-top: var(--sp-2); }
  `]
})
export class KeyringUploadDialogComponent {
  private ref = inject(MatDialogRef<KeyringUploadDialogComponent, KeyringUploadResult | null>);

  file: File | null = null;
  password = '';

  onFile(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.file = input.files && input.files.length ? input.files[0] : null;
  }

  submit(): void {
    if (!this.file || !this.password) return;
    this.ref.close({ file: this.file, password: this.password });
  }

  close(): void {
    this.ref.close(null);
  }
}
