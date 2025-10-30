import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatTableModule } from '@angular/material/table';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatExpansionModule } from '@angular/material/expansion';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { ProjectService, ProjectDto, ProjectDetailsDto } from '../../core/services/project.service';
import { ImportWizardComponent } from './import-wizard/import-wizard.component';
import { ImportJob, ImportStatus } from '../../shared/models/import-job.model';

@Component({
  selector: 'app-projects',
  imports: [
    CommonModule,
    MatCardModule,
    MatButtonModule,
    MatIconModule,
    MatTableModule,
    MatSlideToggleModule,
    MatSnackBarModule,
    MatProgressSpinnerModule,
    MatExpansionModule,
    MatDialogModule
  ],
  templateUrl: './projects.component.html',
  styleUrl: './projects.component.scss'
})
export class ProjectsComponent implements OnInit {
  private projectService = inject(ProjectService);
  private snackBar = inject(MatSnackBar);
  private dialog = inject(MatDialog);

  projects: ProjectDto[] = [];
  expandedProject: ProjectDetailsDto | null = null;
  isLoading = false;

  displayedColumns: string[] = ['name', 'fileName', 'importDate', 'stats', 'isActive', 'actions'];

  ngOnInit() {
    this.loadProjects();
  }

  async loadProjects() {
    try {
      this.isLoading = true;
      this.projects = await this.projectService.getAllProjects().toPromise() || [];
    } catch (error) {
      console.error('Failed to load projects:', error);
      this.snackBar.open('Failed to load projects', 'Close', { duration: 3000 });
    } finally {
      this.isLoading = false;
    }
  }

  openImportWizard() {
    const dialogRef = this.dialog.open(ImportWizardComponent, {
      width: '600px',
      disableClose: true
    });

    dialogRef.afterClosed().subscribe((result: ImportJob | null) => {
      if (result && result.status === ImportStatus.Completed) {
        this.snackBar.open(
          `Projekt "${result.projectName}" erfolgreich importiert! ${result.groupAddressCount} Gruppenadressen und ${result.deviceCount} Geräte gefunden.`,
          'Schließen',
          { duration: 5000 }
        );
        this.loadProjects();
      }
    });
  }

  async toggleActivation(project: ProjectDto) {
    try {
      if (!project.isActive) {
        await this.projectService.activateProject(project.id).toPromise();
        this.snackBar.open(`Project "${project.name}" activated`, 'Close', { duration: 3000 });
        await this.loadProjects();
      }
    } catch (error) {
      console.error('Failed to activate project:', error);
      this.snackBar.open('Failed to activate project', 'Close', { duration: 3000 });
    }
  }

  async deleteProject(project: ProjectDto) {
    if (!confirm(`Are you sure you want to delete project "${project.name}"?`)) {
      return;
    }

    try {
      await this.projectService.deleteProject(project.id).toPromise();
      this.snackBar.open(`Project "${project.name}" deleted`, 'Close', { duration: 3000 });
      await this.loadProjects();
    } catch (error) {
      console.error('Failed to delete project:', error);
      this.snackBar.open('Failed to delete project', 'Close', { duration: 3000 });
    }
  }

  async viewDetails(project: ProjectDto) {
    try {
      this.expandedProject = await this.projectService.getProjectDetails(project.id).toPromise() || null;
    } catch (error) {
      console.error('Failed to load project details:', error);
      this.snackBar.open('Failed to load project details', 'Close', { duration: 3000 });
    }
  }

  closeDetails() {
    this.expandedProject = null;
  }
}
