/** Time-range presets shared by the charts and statistics views. */
export type RangePreset = '1h' | '24h' | '7d' | '30d' | 'custom';

export interface ResolvedRange {
  from: string; // ISO
  to: string; // ISO
}

const PRESET_MS: Record<Exclude<RangePreset, 'custom'>, number> = {
  '1h': 3_600_000,
  '24h': 24 * 3_600_000,
  '7d': 7 * 86_400_000,
  '30d': 30 * 86_400_000,
};

/** Resolves a preset (or custom from/to local-datetime strings) to an ISO range. */
export function resolveRange(
  preset: RangePreset,
  customFrom: string,
  customTo: string
): ResolvedRange | null {
  if (preset === 'custom') {
    const from = localToIso(customFrom);
    const to = localToIso(customTo);
    if (!from || !to) return null;
    return { from, to };
  }
  const now = new Date();
  const from = new Date(now.getTime() - PRESET_MS[preset]);
  return { from: from.toISOString(), to: now.toISOString() };
}

/** Converts a <input type="datetime-local"> value to an ISO string (or null if empty/invalid). */
export function localToIso(local: string): string | null {
  if (!local) return null;
  const d = new Date(local);
  return isNaN(d.getTime()) ? null : d.toISOString();
}
