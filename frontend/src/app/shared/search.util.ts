/**
 * Word-based client-side search, shared by every local search field (charts GA picker, group
 * addresses, monitor live mode, logs) so they all behave the same: every word of the input has
 * to occur somewhere in the text, in any order, and umlauts are optional ("kuche raum" finds
 * "EG.Küche.Heizung.Raumtemperatur").
 */

/**
 * Lower-cases and drops diacritics character by character ("Küche" → "kuche"), so a search
 * works without typing umlauts. Deliberately one output character per input character: the
 * highlighting in the charts GA picker relies on the indices matching the original text.
 */
export function foldForSearch(text: string): string {
  let out = '';
  for (const ch of text.toLowerCase()) {
    const base = ch.normalize('NFD')[0];
    // Characters outside the BMP would count as two here; keep them as they are.
    out += ch.length === 1 && base.length === 1 ? base : ch;
  }
  return out;
}

/** Search input split into words, each folded like the text it is matched against. */
export function searchTokens(input: string): string[] {
  return foldForSearch(input).split(/\s+/).filter((t) => t.length > 0);
}

/** True when every token occurs in the (already folded) haystack. */
export function matchesTokens(foldedHaystack: string, tokens: string[]): boolean {
  return tokens.every((t) => foldedHaystack.includes(t));
}

/**
 * Folds each item's searchable text once and keeps it for the item's lifetime, so filtering on
 * every keystroke or redraw does not fold the same text again. Keyed weakly on the item: a new
 * data load brings new objects, and the old entries go with the old objects.
 */
export class SearchCache<T extends object> {
  private folded = new WeakMap<T, string>();

  constructor(private readonly text: (item: T) => string) {}

  /** Whether the item matches all tokens; an empty token list matches everything. */
  matches(item: T, tokens: string[]): boolean {
    if (tokens.length === 0) return true;
    let haystack = this.folded.get(item);
    if (haystack === undefined) {
      haystack = foldForSearch(this.text(item));
      this.folded.set(item, haystack);
    }
    return matchesTokens(haystack, tokens);
  }
}
