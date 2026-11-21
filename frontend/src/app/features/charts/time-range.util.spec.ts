import { bufferStartAfterRangeStart } from './time-range.util';

describe('bufferStartAfterRangeStart (#43)', () => {
  const buffer = '2026-10-06T12:12:00.000Z';

  it('returns the buffer start when the range reaches further back', () => {
    expect(bufferStartAfterRangeStart('2026-09-10T00:00:00.000Z', buffer)?.toISOString()).toBe(buffer);
  });

  it('stays quiet when the range lies within the buffer', () => {
    expect(bufferStartAfterRangeStart('2026-10-07T00:00:00.000Z', buffer)).toBeNull();
  });

  it('ignores a start less than a minute before the first telegram', () => {
    expect(bufferStartAfterRangeStart('2026-10-06T12:11:30.000Z', buffer)).toBeNull();
  });

  it('says nothing for an empty buffer', () => {
    expect(bufferStartAfterRangeStart('2026-09-10T00:00:00.000Z', null)).toBeNull();
  });
});
