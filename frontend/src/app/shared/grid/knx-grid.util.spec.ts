import { dpt1Labels } from './knx-grid.util';

describe('dpt1Labels', () => {
  it('uses the subtype wording instead of On / Off (#46)', () => {
    expect(dpt1Labels('DPST-1-8', 'en')).toEqual(['Up', 'Down']);
    expect(dpt1Labels('1.008', 'de')).toEqual(['Auf', 'Ab']);
    expect(dpt1Labels('DPST-1-7', 'en')).toEqual(['Decrease', 'Increase']);
    expect(dpt1Labels('DPST-1-1', 'de')).toEqual(['Aus', 'Ein']);
  });

  it('returns null where the caller should fall back to Off / On', () => {
    expect(dpt1Labels('DPT-1', 'en')).toBeNull();
    expect(dpt1Labels('1.999', 'en')).toBeNull();
    expect(dpt1Labels('DPST-9-1', 'en')).toBeNull();
    expect(dpt1Labels(null, 'en')).toBeNull();
  });
});
