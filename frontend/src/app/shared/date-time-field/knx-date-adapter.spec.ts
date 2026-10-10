import { TestBed } from '@angular/core/testing';
import { KnxDateAdapter, dateFieldOrder } from './knx-date-adapter';

describe('KnxDateAdapter', () => {
  let adapter: KnxDateAdapter;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [KnxDateAdapter] });
    adapter = TestBed.inject(KnxDateAdapter);
    adapter.setLocale('de-AT');
  });

  const ymd = (d: Date | null) =>
    d && !isNaN(d.getTime()) ? [d.getFullYear(), d.getMonth() + 1, d.getDate()] : null;

  it('reads the locale field order', () => {
    expect(dateFieldOrder('de-AT')).toEqual(['day', 'month', 'year']);
    expect(dateFieldOrder('en-US')).toEqual(['month', 'day', 'year']);
  });

  it('parses dates in the locale order with any separator', () => {
    expect(ymd(adapter.parse('24.09.2026'))).toEqual([2026, 9, 24]);
    expect(ymd(adapter.parse('24/9/2026'))).toEqual([2026, 9, 24]);
    expect(ymd(adapter.parse('24,09,2026'))).toEqual([2026, 9, 24]);
    expect(ymd(adapter.parse('24092026'))).toEqual([2026, 9, 24]);
    expect(ymd(adapter.parse('2026-09-24'))).toEqual([2026, 9, 24]);
    adapter.setLocale('en-US');
    expect(ymd(adapter.parse('09/24/2026'))).toEqual([2026, 9, 24]);
  });

  it('rejects anything but a real date with a four-digit year (#41)', () => {
    for (const text of ['08.10.202600', '24.09.26', '31.02.2026', '00.01.2026', '1.13.2026', 'abc', '24.09']) {
      const parsed = adapter.parse(text);
      expect(parsed && adapter.isValid(parsed)).withContext(text).toBeFalsy();
    }
    expect(adapter.parse('')).toBeNull();
  });

  it('parses 24-hour times only', () => {
    const hm = (t: string) => {
      const d = adapter.parseTime(t);
      return d && adapter.isValid(d) ? [d.getHours(), d.getMinutes(), d.getSeconds()] : null;
    };
    expect(hm('14:30')).toEqual([14, 30, 0]);
    expect(hm('14.30')).toEqual([14, 30, 0]);
    expect(hm('0:05:09')).toEqual([0, 5, 9]);
    expect(hm('930')).toEqual([9, 30, 0]);
    expect(hm('24:00')).toBeNull();
    expect(hm('12:60')).toBeNull();
    expect(hm('2:30 PM')).toBeNull();
  });

  it('formats a four-digit year and a 24-hour clock', () => {
    const d = new Date(2026, 8, 24, 0, 5);
    expect(adapter.format(d, { day: '2-digit', month: '2-digit', year: 'numeric' })).toBe('24.09.2026');
    adapter.setLocale('en-US');
    expect(adapter.format(d, { hour: '2-digit', minute: '2-digit', hourCycle: 'h23' })).toBe('00:05');
  });
});
