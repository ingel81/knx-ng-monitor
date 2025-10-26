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
import { ProjectService, ProjectDto, ProjectDetailsDto } from '../../core/services/project.service';

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
    MatExpansionModule
  ],
  templateUrl: './projects.component.html',
  styleUrl: './projects.component.scss'
})
export class ProjectsComponent implements OnInit {
  private projectService = inject(ProjectService);
  private snackBar = inject(MatSnackBar);

  projects: ProjectDto[] = [];
  expandedProject: ProjectDetailsDto | null = null;
  isLoading = false;
  isUploading = false;

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

  onFileSelected(event: Event) {
    const input = event.target as HTMLInputElement;
    if (input.files && input.files.length > 0) {
      this.uploadProject(input.files[0]);
      input.value = ''; // Reset input
    }
  }

  async uploadProject(file: File) {
    try {
      this.isUploading = true;

      const project = await this.projectService.uploadProject(file).toPromise();

      if (project) {
        this.snackBar.open(
          `Project "${project.name}" uploaded successfully! Found ${project.groupAddressCount} group addresses and ${project.deviceCount} devices.`,
          'Close',
          { duration: 5000 }
        );

        await this.loadProjects();
      }
    } catch (error: any) {
      console.error('Failed to upload project:', error);
      const errorMessage = error?.error?.error || 'Failed to upload project file';
      this.snackBar.open(errorMessage, 'Close', { duration: 5000 });
    } finally {
      this.isUploading = false;
    }
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
