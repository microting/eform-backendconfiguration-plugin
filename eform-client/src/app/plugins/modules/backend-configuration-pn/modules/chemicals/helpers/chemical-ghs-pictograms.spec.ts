import {describe, it, expect} from 'vitest';
import {ghsPictogramSrc} from './chemical-ghs-pictograms';

/** The GHS diamonds of W1/W2 (855 check G1): the official artwork, vendored; an unknown code stays a text chip. */
describe('ghsPictogramSrc', () => {
  const codes = ['GHS01', 'GHS02', 'GHS03', 'GHS04', 'GHS05', 'GHS06', 'GHS07', 'GHS08', 'GHS09'];

  it.each(codes)('%s is the vendored SVG as a data URL (no runtime fetch)', (code) => {
    const src = ghsPictogramSrc(code);

    expect(src).toMatch(/^data:image\/svg\+xml;base64,/);
    expect(atob((src as string).split(',')[1])).toContain('<svg');
  });

  it('gives every pictogram its own artwork', () => {
    expect(new Set(codes.map(ghsPictogramSrc)).size).toBe(codes.length);
  });

  it('reads the code leniently, as the register may send it', () => {
    expect(ghsPictogramSrc(' ghs07 ')).toBe(ghsPictogramSrc('GHS07'));
  });

  it.each(['GHS10', 'GHS00', 'H302', '', null, undefined])('has no artwork for %s', (code) => {
    expect(ghsPictogramSrc(code)).toBeNull();
  });
});
