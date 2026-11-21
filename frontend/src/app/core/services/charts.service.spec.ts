import { extractNumeric } from './charts.service';

describe('extractNumeric for 1-bit subtypes', () => {
  it('maps Up / Down and Open / Close the way the bus does, not by word lists (#46)', () => {
    expect(extractNumeric('Up', 'DPST-1-8')).toBe(0);
    expect(extractNumeric('Down', 'DPST-1-8')).toBe(1);
    expect(extractNumeric('Open', '1.009')).toBe(0);
    expect(extractNumeric('Close', '1.009')).toBe(1);
  });

  it('charts subtypes that no word list knew', () => {
    expect(extractNumeric('Increase', 'DPST-1-7')).toBe(1);
    expect(extractNumeric('Night', 'DPST-1-24')).toBe(1);
    expect(extractNumeric('cooling', 'DPST-1-100')).toBe(0);
  });

  it('keeps the word lists for telegrams without a DPT', () => {
    expect(extractNumeric('On')).toBe(1);
    expect(extractNumeric('21.5 °C', 'DPST-9-1')).toBe(21.5);
  });
});
