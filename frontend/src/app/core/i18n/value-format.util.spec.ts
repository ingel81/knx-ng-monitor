import { formatDecodedValue } from './value-format.util';

describe('formatDecodedValue (#44)', () => {
  const de = 'de-DE';
  const en = 'en-GB';

  it('localises the decimal separator and keeps the decimals', () => {
    expect(formatDecodedValue('847.2 W', 'DPST-14-56', de)).toBe('847,2 W');
    expect(formatDecodedValue('80.12%', 'DPST-9-7', de)).toBe('80,12 %');
    expect(formatDecodedValue('80.12%', 'DPST-9-7', en)).toBe('80.12%');
    expect(formatDecodedValue('0.0%', 'DPST-5-1', de)).toBe('0,0 %');
    expect(formatDecodedValue('-3.50 °C', 'DPST-9-1', de)).toBe('-3,50 °C');
    expect(formatDecodedValue('847.2 W', 'DPST-14-56', en)).toBe('847.2 W');
  });

  it('adds no grouping to long counters', () => {
    expect(formatDecodedValue('31146411 Wh', 'DPST-13-10', de)).toBe('31146411 Wh');
  });

  it('formats time of day (DPT 10) with a local weekday, always 24 h', () => {
    expect(formatDecodedValue('Wed 08:11:51', 'DPST-10-1', de)).toBe('Mi., 08:11:51');
    expect(formatDecodedValue('Sun 21:05:00', 'DPST-10-1', 'en-US')).toContain('21:05:00');
    expect(formatDecodedValue('08:11:51', 'DPST-10-1', de)).toBe('08:11:51');
  });

  it('formats dates (DPT 11) and date-times (DPT 19) in the local order', () => {
    expect(formatDecodedValue('2026-09-16', 'DPST-11-1', de)).toBe('16.09.2026');
    expect(formatDecodedValue('2026-09-16', 'DPST-11-1', en)).toBe('16/09/2026');
    expect(formatDecodedValue('2026-09-16 Wed 08:20:06', 'DPST-19-1', de)).toBe('Mi., 16.09.2026, 08:20:06');
  });

  it('leaves everything it does not recognise unchanged', () => {
    expect(formatDecodedValue('wechselhaft 1.5', 'DPST-16-0', de)).toBe('wechselhaft 1.5');
    expect(formatDecodedValue('+ 100%', 'DPST-3-7', de)).toBe('+ 100%');
    expect(formatDecodedValue('0x21', null, de)).toBe('0x21');
    expect(formatDecodedValue('21.5', null, de)).toBe('21.5');
    expect(formatDecodedValue('No day 08:11:51', 'DPST-10-1', de)).toBe('No day 08:11:51');
    expect(formatDecodedValue('2026-02-30', 'DPST-11-1', de)).toBe('2026-02-30');
  });
});
