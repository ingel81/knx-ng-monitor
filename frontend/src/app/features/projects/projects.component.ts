import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatTableModule } from '@angular/material/table';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatExpansionModule } from '@angular/material/expansion';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { ProjectService, ProjectDto, ProjectDetailsDto } from '../../core/services/project.service';
import { ImportWizardComponent } from './import-wizard/import-wizard.component';
import { ImportJob, ImportStatus } from '../../shared/models/import-job.model';
import { ConfirmDialogComponent, ConfirmDialogData } from '../../shared/confirm-dialog.component';
import { KeyringUploadDialogComponent, KeyringUploadResult } from './keyring-upload-dialog.component';
import { LanguageService } from '../../core/i18n/language.service';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { KnxDatePipe } from '../../core/i18n/date.pipe';
import { LoggerService } from '../../core/logging/logger.service';

@Component({
  selector: 'app-projects',
  imports: [
    CommonModule,
    MatCardModule,
    MatButtonModule,
    MatIconModule,
    MatTableModule,
    MatTooltipModule,
    MatSnackBarModule,
    MatProgressSpinnerModule,
    MatExpansionModule,
    MatDialogModule,
    TranslatePipe,
    KnxDatePipe
  ],
  templateUrl: './projects.component.html',
  styleUrl: './projects.component.scss'
})
export class ProjectsComponent implements OnInit {
  private projectService = inject(ProjectService);
  private snackBar = inject(MatSnackBar);
  private dialog = inject(MatDialog);
  private lang = inject(LanguageService);
  private logger = inject(LoggerService);

  private snack(message: string): void {
    this.snackBar.open(message, this.lang.translate('common.close'), { duration: 3000 });
  }

  projects: ProjectDto[] = [];
  expandedProject: ProjectDetailsDto | null = null;
  isLoading = false;
  deletingId: number | null = null;
  togglingId: number | null = null;

  displayedColumns: string[] = ['name', 'fileName', 'importDate', 'stats', 'isActive', 'actions'];

  ngOnInit() {
    this.loadProjects();
  }

  async loadProjects() {
    try {
      this.isLoading = true;
      this.projects = await this.projectService.getAllProjects().toPromise() || [];
    } catch (error) {
      this.logger.error('Failed to load projects:', error);
      this.snack(this.lang.translate('projects.loadFailed'));
    } finally {
      this.isLoading = false;
    }
  }

  get activeProject(): ProjectDto | null {
    return this.projects.find(p => p.isActive) ?? null;
  }


  openImportWizard() {
    const dialogRef = this.dialog.open(ImportWizardComponent, {
      width: '600px',
      disableClose: true
    });

    dialogRef.afterClosed().subscribe((result: ImportJob | null) => {
      if (result && result.status === ImportStatus.Completed) {
        this.snackBar.open(
          this.lang.translate('projects.imported', {
            name: result.projectName ?? '',
            gas: result.groupAddressCount ?? 0,
            devices: result.deviceCount ?? 0
          }),
          this.lang.translate('common.close'),
          { duration: 5000 }
        );
        this.loadProjects();
      }
    });
  }

  async toggleActivation(project: ProjectDto) {
    if (this.togglingId !== null) {
      return;
    }
    this.togglingId = project.id;
    try {
      if (project.isActive) {
        // Deactivating the active project severs the bus link (model: active ⇒ connected).
        await this.projectService.deactivateProject(project.id).toPromise();
        this.snack(this.lang.translate('projects.deactivated', { name: project.name }));
      } else {
        await this.projectService.activateProject(project.id).toPromise();
        this.snack(this.lang.translate('projects.activated', { name: project.name }));
      }
      await this.loadProjects();
    } catch (error) {
      this.logger.error('Failed to toggle project activation:', error);
      this.snack(this.lang.translate('projects.activationFailed'));
    } finally {
      this.togglingId = null;
    }
  }

  async deleteProject(project: ProjectDto) {
    // Fetch what the deletion affects so the confirmation can show real counts.
    let preview;
    try {
      preview = await this.projectService.getDeletePreview(project.id).toPromise();
    } catch (error) {
      this.logger.error('Failed to load delete preview:', error);
      this.snack(this.lang.translate('projects.deletePrepareFailed'));
      return;
    }

    const data: ConfirmDialogData = {
      title: this.lang.translate('projects.deleteTitle', { name: project.name }),
      message: this.lang.translate('projects.deleteMsg', {
        gas: preview?.groupAddressCount ?? 0,
        devices: preview?.deviceCount ?? 0,
        telegrams: preview?.telegramCount ?? 0
      }),
      warning: this.lang.translate('projects.deleteWarning'),
      confirmText: this.lang.translate('projects.delete'),
      danger: true
    };

    const confirmed = await this.dialog
      .open(ConfirmDialogComponent, { data, width: '440px' })
      .afterClosed()
      .toPromise();

    if (!confirmed) {
      return;
    }

    this.deletingId = project.id;
    try {
      await this.projectService.deleteProject(project.id).toPromise();
      this.snack(this.lang.translate('projects.deleted', { name: project.name }));
      await this.loadProjects();
    } catch (error) {
      this.logger.error('Failed to delete project:', error);
      this.snack(this.lang.translate('projects.deleteFailed'));
    } finally {
      this.deletingId = null;
    }
  }

  uploadingKeyringId: number | null = null;

  async uploadKeyring(project: ProjectDto) {
    const result = await this.dialog
      .open(KeyringUploadDialogComponent, { width: '440px', autoFocus: false })
      .afterClosed()
      .toPromise() as KeyringUploadResult | null | undefined;

    if (!result) {
      return;
    }

    this.uploadingKeyringId = project.id;
    try {
      const res = await this.projectService.uploadKeyring(project.id, result.file, result.password).toPromise();
      this.snack(this.lang.translate('projects.keyringSuccess', {
        total: res?.totalKeys ?? 0,
        gas: res?.groupAddressKeys ?? 0,
        tool: res?.toolKeys ?? 0
      }));
    } catch (error) {
      this.logger.error('Failed to upload keyring:', error);
      this.snack(this.lang.translate('projects.keyringFailed'));
    } finally {
      this.uploadingKeyringId = null;
    }
  }

  async viewDetails(project: ProjectDto) {
    try {
      this.expandedProject = await this.projectService.getProjectDetails(project.id).toPromise() || null;
    } catch (error) {
      this.logger.error('Failed to load project details:', error);
      this.snack(this.lang.translate('projects.detailsFailed'));
    }
  }

  closeDetails() {
    this.expandedProject = null;
  }
}
