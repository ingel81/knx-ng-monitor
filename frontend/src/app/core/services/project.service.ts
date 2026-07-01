import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment.development';
import { ImportJob, ProvideInput } from '../../shared/models/import-job.model';

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

export interface ProjectDeletePreview {
  groupAddressCount: number;
  deviceCount: number;
  /** Recorded telegrams mapped to this project; kept on delete but lose their name mapping. */
  telegramCount: number;
}


/** Flat node of the building/location tree (rebuilt client-side via externalId/parentExternalId). */
export interface LocationDto {
  id: number;
  externalId: string;
  name: string;
  type: string;
  parentExternalId?: string | null;
  deviceAddresses: string[];
  groupAddresses: string[];
}

export interface CommunicationObjectDto {
  id: number;
  deviceAddress: string;
  deviceName?: string | null;
  number: number;
  name?: string | null;
  functionText?: string | null;
  groupAddressLink: string;
  datapointType?: string | null;
  flags?: string | null;
}

/** A KNX GroupRange = main/middle group name spanning a contiguous block of GA addresses. */
export interface GroupRangeDto {
  name: string;
  rangeStart: number;
  rangeEnd: number;
}

export interface KeyringUploadResultDto {
  totalKeys: number;
  groupAddressKeys: number;
  toolKeys: number;
  hasBackboneKey: boolean;
}

@Injectable({
  providedIn: 'root'
})
export class ProjectService {
  private http = inject(HttpClient);
  private apiUrl = `${environment.apiUrl}/projects`;

  uploadProject(file: File): Observable<ImportJob> {
    const formData = new FormData();
    formData.append('file', file, file.name);
    return this.http.post<ImportJob>(`${this.apiUrl}/upload`, formData);
  }

  getImportStatus(jobId: string): Observable<ImportJob> {
    return this.http.get<ImportJob>(`${this.apiUrl}/imports/${jobId}`);
  }

  provideInput(jobId: string, input: ProvideInput): Observable<{ message: string }> {
    return this.http.post<{ message: string }>(`${this.apiUrl}/imports/${jobId}/provide-input`, input);
  }

  cancelImport(jobId: string): Observable<{ message: string }> {
    return this.http.delete<{ message: string }>(`${this.apiUrl}/imports/${jobId}`);
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

  deactivateProject(id: number): Observable<{ message: string }> {
    return this.http.put<{ message: string }>(`${this.apiUrl}/${id}/deactivate`, {});
  }

  getDeletePreview(id: number): Observable<ProjectDeletePreview> {
    return this.http.get<ProjectDeletePreview>(`${this.apiUrl}/${id}/delete-preview`);
  }

  deleteProject(id: number): Observable<{ message: string }> {
    return this.http.delete<{ message: string }>(`${this.apiUrl}/${id}`);
  }

  // --- Locations (building tree) / communication objects / keyring -----------

  getLocations(projectId: number): Observable<LocationDto[]> {
    return this.http.get<LocationDto[]>(`${this.apiUrl}/${projectId}/locations`);
  }

  getGroupRanges(projectId: number): Observable<GroupRangeDto[]> {
    return this.http.get<GroupRangeDto[]>(`${this.apiUrl}/${projectId}/groupranges`);
  }

  getCommObjects(projectId: number, address?: string): Observable<CommunicationObjectDto[]> {
    const query = address ? `?address=${encodeURIComponent(address)}` : '';
    return this.http.get<CommunicationObjectDto[]>(`${this.apiUrl}/${projectId}/commobjects${query}`);
  }

  /** Resolves a single device of a project by its exact physical address (e.g. "1.0.59"). */
  getDeviceByAddress(projectId: number, address: string): Observable<DeviceDto> {
    return this.http.get<DeviceDto>(`${this.apiUrl}/${projectId}/device?address=${encodeURIComponent(address)}`);
  }

  uploadKeyring(projectId: number, file: File, password: string): Observable<KeyringUploadResultDto> {
    const formData = new FormData();
    formData.append('file', file, file.name);
    formData.append('password', password);
    return this.http.post<KeyringUploadResultDto>(`${this.apiUrl}/${projectId}/keyring`, formData);
  }
}
