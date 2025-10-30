import { Component, OnInit, OnDestroy, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatDialogRef, MatDialogModule } from '@angular/material/dialog';
import { MatStepperModule } from '@angular/material/stepper';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatIconModule } from '@angular/material/icon';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { FormsModule } from '@angular/forms';
import { Subject, takeUntil } from 'rxjs';
import { ProjectService } from '../../../core/services/project.service';
import { ImportPollingService } from '../../../core/services/import-polling.service';
import { ImportJob, ImportStatus, RequirementType, ImportStep, EtsVersion } from '../../../shared/models/import-job.model';

@Component({
  selector: 'app-import-wizard',
  standalone: true,
  imports: [
    CommonModule,
    MatDialogModule,
    MatStepperModule,
    MatButtonModule,
    MatProgressBarModule,
    MatIconModule,
    MatFormFieldModule,
    MatInputModule,
    FormsModule
  ],
  templateUrl: './import-wizard.component.html',
  styleUrls: ['./import-wizard.component.scss']
})
export class ImportWizardComponent implements OnInit, OnDestroy {
  private dialogRef = inject(MatDialogRef<ImportWizardComponent>);
  private projectService = inject(ProjectService);
  private pollingService = inject(ImportPollingService);
  private destroy$ = new Subject<void>();

  selectedFile: File | null = null;
  importJob: ImportJob | null = null;
  currentStep: 'file-selection' | 'analyzing' | 'requirements' | 'importing' | 'complete' = 'file-selection';

  // Requirements inputs
  projectPassword = '';
  keyringFile: File | null = null;
  keyringPassword = '';
  passwordError = '';

  // Expose enums to template
  ImportStatus = ImportStatus;
  RequirementType = RequirementType;

  ngOnInit(): void {}

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
    this.pollingService.stopPolling();
  }

  onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    if (input.files && input.files.length > 0) {
      this.selectedFile = input.files[0];
    }
  }

  onKeyringFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    if (input.files && input.files.length > 0) {
      this.keyringFile = input.files[0];
    }
  }

  startImport(): void {
    if (!this.selectedFile) return;

    this.currentStep = 'analyzing';

    this.projectService.uploadProject(this.selectedFile)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (job) => {
          this.importJob = job;
          this.startPolling(job.id);
        },
        error: (error) => {
          console.error('Upload failed:', error);
          // TODO: Show error message
        }
      });
  }

  private startPolling(jobId: string): void {
    this.pollingService.startPolling(jobId, 500)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (job) => {
          this.importJob = job;
          this.updateCurrentStep(job);
        },
        error: (error) => {
          console.error('Polling error:', error);
        }
      });
  }

  private updateCurrentStep(job: ImportJob): void {
    if (job.status === ImportStatus.WaitingForInput) {
      this.currentStep = 'requirements';
    } else if (job.status === ImportStatus.Importing) {
      this.currentStep = 'importing';
    } else if (job.status === ImportStatus.Completed) {
      this.currentStep = 'complete';
      this.pollingService.stopPolling();
    } else if (job.status === ImportStatus.Failed) {
      this.currentStep = 'complete';
      this.pollingService.stopPolling();
    }
  }

  submitPassword(): void {
    if (!this.importJob || !this.projectPassword) return;

    this.passwordError = '';

    this.projectService.provideInput(this.importJob.id, {
      type: RequirementType.ProjectPassword,
      password: this.projectPassword
    })
    .pipe(takeUntil(this.destroy$))
    .subscribe({
      next: () => {
        // Input accepted, continue polling
        this.currentStep = 'importing';
      },
      error: (error) => {
        this.passwordError = 'Falsches Passwort. Bitte versuchen Sie es erneut.';
        console.error('Failed to provide password:', error);
      }
    });
  }

  async submitKeyring(): Promise<void> {
    if (!this.importJob || !this.keyringFile || !this.keyringPassword) return;

    const keyringData = await this.readFileAsArrayBuffer(this.keyringFile);
    const base64Data = this.arrayBufferToBase64(keyringData);

    this.projectService.provideInput(this.importJob.id, {
      type: RequirementType.KeyringFile,
      keyringFile: base64Data
    })
    .pipe(takeUntil(this.destroy$))
    .subscribe({
      next: () => {
        // Submit keyring password
        this.submitKeyringPassword();
      },
      error: (error) => {
        console.error('Failed to provide keyring:', error);
      }
    });
  }

  private submitKeyringPassword(): void {
    if (!this.importJob || !this.keyringPassword) return;

    this.projectService.provideInput(this.importJob.id, {
      type: RequirementType.KeyringPassword,
      password: this.keyringPassword
    })
    .pipe(takeUntil(this.destroy$))
    .subscribe({
      next: () => {
        this.currentStep = 'importing';
      },
      error: (error) => {
        console.error('Failed to provide keyring password:', error);
      }
    });
  }

  private readFileAsArrayBuffer(file: File): Promise<ArrayBuffer> {
    return new Promise((resolve, reject) => {
      const reader = new FileReader();
      reader.onload = () => resolve(reader.result as ArrayBuffer);
      reader.onerror = reject;
      reader.readAsArrayBuffer(file);
    });
  }

  private arrayBufferToBase64(buffer: ArrayBuffer): string {
    let binary = '';
    const bytes = new Uint8Array(buffer);
    const len = bytes.byteLength;
    for (let i = 0; i < len; i++) {
      binary += String.fromCharCode(bytes[i]);
    }
    return btoa(binary);
  }

  cancel(): void {
    if (this.importJob && this.importJob.status !== ImportStatus.Completed) {
      this.projectService.cancelImport(this.importJob.id).subscribe();
    }
    this.dialogRef.close();
  }

  close(): void {
    this.dialogRef.close(this.importJob?.status === ImportStatus.Completed ? this.importJob : null);
  }

  getStepStatus(step: ImportStep): string {
    if (step.status === 'completed') return '✓';
    if (step.status === 'in-progress') return '...';
    if (step.status === 'failed') return '✗';
    return '○';
  }

  getStepClass(step: ImportStep): string {
    return `step-${step.status}`;
  }

  hasRequirement(type: RequirementType): boolean {
    return this.importJob?.requirements.some(r => r.type === type && !r.isFulfilled) || false;
  }

  getEtsVersionLabel(version?: EtsVersion): string {
    switch (version) {
      case EtsVersion.Ets4:
        return 'ETS 4';
      case EtsVersion.Ets5:
        return 'ETS 5';
      case EtsVersion.Ets6:
        return 'ETS 6';
      case EtsVersion.Unknown:
        return 'Unbekannt';
      default:
        return 'Unbekannt';
    }
  }
}
