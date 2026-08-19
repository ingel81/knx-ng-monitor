/**
 * Formatiert eine Dauer in Minuten für die Anzeige: "11 h 45 min", "45 min", "30 s".
 *
 * Reine Minutenangaben lesen sich an beiden Enden schlecht — "705 min" muss man umrechnen,
 * "0,5 min" ist keine Angabe. Einheiten bleiben sprachneutral (h/min/s), damit dieselbe
 * Ausgabe in beiden UI-Sprachen trägt.
 */
export function formatDurationMinutes(minutes: number): string {
  if (!isFinite(minutes) || minutes <= 0) return '0 min';
  if (minutes < 1) return `${Math.max(1, Math.round(minutes * 60))} s`;

  const total = Math.round(minutes);
  const hours = Math.floor(total / 60);
  const rest = total % 60;
  if (hours === 0) return `${rest} min`;
  if (rest === 0) return `${hours} h`;
  return `${hours} h ${rest} min`;
}
