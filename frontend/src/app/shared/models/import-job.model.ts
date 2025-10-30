export enum ImportStatus {
  Analyzing = 'Analyzing',
  WaitingForInput = 'WaitingForInput',
  Importing = 'Importing',
  Completed = 'Completed',
  Failed = 'Failed',
  Cancelled = 'Cancelled'
}

export enum RequirementType {
  ProjectPassword = 'ProjectPassword',
  KeyringFile = 'KeyringFile',
  KeyringPassword = 'KeyringPassword'
}

export enum ImportStepType {
  UploadFile = 'UploadFile',
  OpenZip = 'OpenZip',
  DetectFeatures = 'DetectFeatures',
  CheckPassword = 'CheckPassword',
  ParseGroupAddresses = 'ParseGroupAddresses',
  ParseDevices = 'ParseDevices',
  ParseSecurity = 'ParseSecurity',
  Validate = 'Validate',
  Save = 'Save',
  RefreshCache = 'RefreshCache'
}

export enum EtsVersion {
  Unknown = 'Unknown',
  Ets4 = 'Ets4',
  Ets5 = 'Ets5',
  Ets6 = 'Ets6'
}

export interface ImportStep {
  type: ImportStepType;
  name: string;
  status: 'pending' | 'in-progress' | 'completed' | 'failed';
  progress: number;
  startTime?: string;
  endTime?: string;
  errorMessage?: string;
}

export interface ImportRequirement {
  type: RequirementType;
  message: string;
  isFulfilled: boolean;
  remainingAttempts: number;
}

export interface ImportJob {
  id: string;
  status: ImportStatus;
  overallProgress: number;
  steps: ImportStep[];
  requirements: ImportRequirement[];
  createdAt: string;
  completedAt?: string;
  errorMessage?: string;
  projectId?: number;
  projectName?: string;
  groupAddressCount?: number;
  deviceCount?: number;
  etsVersion?: EtsVersion;
  hasKnxSecure: boolean;
}

export interface ProvideInput {
  type: RequirementType;
  password?: string;
  keyringFile?: string; // Base64-encoded
  keyringPassword?: string;
}
