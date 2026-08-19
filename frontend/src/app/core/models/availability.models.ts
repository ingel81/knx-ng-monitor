/**
 * Availability of the recording, mirrors Core.DTOs.AvailabilityResponse.
 *
 * Exists because an empty stretch of telegram history is ambiguous on its own: it can mean a quiet
 * bus, a lost bus link, or a monitor that was not running at all. The backend derives these states
 * from the one-per-minute heartbeats.
 */
export type AvailabilityState = 'Up' | 'BusDown' | 'MonitorDown' | 'Unknown';

export interface AvailabilityInterval {
  from: string;
  to: string;
  state: AvailabilityState;
  minutes: number;
  telegrams: number;
}

export interface Availability {
  from: string;
  to: string;
  heartbeatIntervalSeconds: number;
  intervals: AvailabilityInterval[];
  upMinutes: number;
  busDownMinutes: number;
  monitorDownMinutes: number;
  /** Nicht abgedeckte Zeit: vor Beginn der Aufzeichnung oder jenseits ihrer Aufbewahrung. */
  unknownMinutes: number;
  /** Nur belegte Ausfälle (`BusDown`/`MonitorDown`), ohne `Unknown`. */
  outages: AvailabilityInterval[];
}
