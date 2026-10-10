import { escapeHtml } from './html.util';

describe('escapeHtml', () => {
  it('neutralises markup from project names', () => {
    expect(escapeHtml('<img src=x onerror="alert(1)">')).toBe('&lt;img src=x onerror=&quot;alert(1)&quot;&gt;');
  });

  it('keeps plain names and umlauts', () => {
    expect(escapeHtml('EG Küche Temperatur')).toBe('EG Küche Temperatur');
  });

  it('turns null and numbers into text', () => {
    expect(escapeHtml(null)).toBe('');
    expect(escapeHtml(21.5)).toBe('21.5');
  });
});
