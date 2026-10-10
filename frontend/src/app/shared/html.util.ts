/**
 * Escapes text for HTML that is assembled as a string, e.g. ECharts tooltips or force-graph
 * labels, which render through innerHTML. Group-address and device names come from the imported
 * ETS project, so they are untrusted input there.
 */
export function escapeHtml(value: unknown): string {
  return String(value ?? '')
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&#39;');
}
