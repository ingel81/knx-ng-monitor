export interface RecordingSettings {
  hotBufferMaxCount: number;
  archiveEnabled: boolean;
  archiveRetentionDays: number | null;
  /** Seconds telegrams are collected before they are written to the history; 0 = right away. */
  historyWriteIntervalSeconds: number;
}
