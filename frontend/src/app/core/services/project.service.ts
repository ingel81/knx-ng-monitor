import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

export interface ProjectDto {
  id: number;
  name: string;
  fileName: string;
  importDate: Date;
  isActive: boolean;
  groupAddressCount: number;
  deviceCount: number;
}

export interface GroupAddressDto {
  id: number;
  address: string;
  name: string;
  description?: string;
  datapointType?: string;
}

export interface DeviceDto {
  id: number;
  name: string;
  physicalAddress: string;
  manufacturer?: string;
  productName?: string;
}

export interface ProjectDetailsDto {
  id: number;
  name: string;
  fileName: string;
  importDate: Date;
  isActive: boolean;
  groupAddresses: GroupAddressDto[];
  devices: DeviceDto[];
}

@Injectable({
  providedIn: 'root'
})
export class ProjectService {
  private http = inject(HttpClient);
  private apiUrl = 'http://localhost:5075/api/projects';

  uploadProject(file: File): Observable<ProjectDto> {
    const formData = new FormData();
    formData.append('file', file, file.name);
    return this.http.post<ProjectDto>(`${this.apiUrl}/upload`, formData);
  }

  getAllProjects(): Observable<ProjectDto[]> {
    return this.http.get<ProjectDto[]>(this.apiUrl);
  }

  getProjectDetails(id: number): Observable<ProjectDetailsDto> {
    return this.http.get<ProjectDetailsDto>(`${this.apiUrl}/${id}`);
  }

  activateProject(id: number): Observable<{ message: string }> {
    return this.http.put<{ message: string }>(`${this.apiUrl}/${id}/activate`, {});
  }

  deleteProject(id: number): Observable<{ message: string }> {
    return this.http.delete<{ message: string }>(`${this.apiUrl}/${id}`);
  }
}
