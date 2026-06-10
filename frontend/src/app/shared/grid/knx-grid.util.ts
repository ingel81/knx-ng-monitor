/** Normalisiert messageType (Enum-Zahl oder String) auf einen Kurz-Key. */
export function messageTypeKind(type: string | number | undefined): '' | 'write' | 'read' | 'response' {
  switch (String(type).toLowerCase()) {
    case 'write': case '0': return 'write';
    case 'read': case '1': return 'read';
    case 'response': case '2': return 'response';
    default: return '';
  }
}

/** Row-/Cell-Klasse für den Nachrichtentyp (msg-write|read|response). */
export function messageTypeClass(type: string | number | undefined): string {
  const kind = messageTypeKind(type);
  return kind ? `msg-${kind}` : '';
}

/** Anzeigename des Nachrichtentyps. */
export function messageTypeName(type: string | number | undefined): string {
  const kind = messageTypeKind(type);
  if (kind) return kind.charAt(0).toUpperCase() + kind.slice(1);
  return type === undefined ? '' : String(type);
}

// --- DPST -> Einheit ---------------------------------------------------------
// Schlüssel "main.sub" (3-stellig). Nur „Zahl+Einheit"-DPTs; %-/Winkel-DPTs
// (5.x/6.x) bringen die Einheit bereits im dekodierten String mit.
const DPT_UNITS: Record<string, string> = {
  '7.011': 'mm', '7.012': 'mA', '7.013': 'lx', '7.600': 'K',
  '8.002': 'ms', '8.003': 's', '8.004': 's', '8.005': 's', '8.012': 'm',
  '9.001': '°C', '9.002': 'K', '9.003': 'K/h', '9.004': 'lx', '9.005': 'm/s',
  '9.006': 'Pa', '9.007': '%', '9.008': 'ppm', '9.009': 'm³/h', '9.010': 's',
  '9.011': 'ms', '9.020': 'mV', '9.021': 'mA', '9.022': 'W/m²', '9.024': 'kW',
  '9.025': 'l/h', '9.026': 'l/m²', '9.027': '°F', '9.028': 'km/h',
  '12.100': 's', '12.101': 'min', '12.102': 'h',
  '13.002': 'm³/h', '13.010': 'Wh', '13.011': 'VAh', '13.012': 'varh',
  '13.013': 'kWh', '13.014': 'kVAh', '13.015': 'kvarh', '13.016': 'MWh', '13.100': 's',
  '14.019': 'A', '14.027': 'V', '14.028': 'V', '14.031': 'J', '14.033': 'Hz',
  '14.038': 'Ω', '14.039': 'm', '14.051': 'kg', '14.056': 'W', '14.057': 'VA',
  '14.058': 'var', '14.065': 'm/s', '14.068': '°C', '14.076': 'm³',
};

/** Einheit zu einem DPT-String (format-agnostisch: "DPST-14-56", "DPT 9.001", "9.001"). */
export function unitForDpt(dpt: string | undefined | null): string {
  if (!dpt) return '';
  const m = dpt.toString().match(/(\d+)\D+(\d+)/);
  if (!m) return '';
  const key = `${parseInt(m[1], 10)}.${String(parseInt(m[2], 10)).padStart(3, '0')}`;
  return DPT_UNITS[key] ?? '';
}
