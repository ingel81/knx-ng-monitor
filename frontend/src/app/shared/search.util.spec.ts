import { SearchCache, foldForSearch, matchesTokens, searchTokens } from './search.util';

describe('search.util', () => {
  const ga = { text: '1/2/3 EG.Küche.Heizung.Raumtemperatur' };

  describe('foldForSearch', () => {
    it('lower-cases and drops diacritics', () => {
      expect(foldForSearch('Küche ÖL')).toBe('kuche ol');
    });

    it('keeps one character per character, so highlight indices stay valid', () => {
      const text = 'Straße Ö';
      expect(foldForSearch(text).length).toBe(text.length);
    });
  });

  describe('searchTokens', () => {
    it('splits on whitespace and folds every word', () => {
      expect(searchTokens('  RAUM   küche ')).toEqual(['raum', 'kuche']);
    });

    it('returns nothing for blank input', () => {
      expect(searchTokens('   ')).toEqual([]);
    });
  });

  describe('matchesTokens', () => {
    it('needs every token, in any order', () => {
      const haystack = foldForSearch(ga.text);
      expect(matchesTokens(haystack, ['raum', 'kuche'])).toBeTrue();
      expect(matchesTokens(haystack, ['raum', 'bad'])).toBeFalse();
    });
  });

  describe('SearchCache', () => {
    const cache = new SearchCache<{ text: string }>((x) => x.text);

    it('finds words in any order without typing umlauts', () => {
      expect(cache.matches(ga, searchTokens('kuche raum'))).toBeTrue();
      expect(cache.matches(ga, searchTokens('RAUM küche'))).toBeTrue();
    });

    it('matches address parts', () => {
      expect(cache.matches(ga, searchTokens('1/2'))).toBeTrue();
    });

    it('rejects a missing word', () => {
      expect(cache.matches(ga, searchTokens('bad'))).toBeFalse();
    });

    it('lets everything through for an empty search', () => {
      expect(cache.matches(ga, searchTokens('  '))).toBeTrue();
    });

    it('folds each item only once', () => {
      const text = jasmine.createSpy('text').and.callFake((x: { text: string }) => x.text);
      const counting = new SearchCache<{ text: string }>(text);
      const item = { text: 'Licht Flur' };
      counting.matches(item, ['licht']);
      counting.matches(item, ['flur']);
      expect(text).toHaveBeenCalledTimes(1);
    });
  });
});
