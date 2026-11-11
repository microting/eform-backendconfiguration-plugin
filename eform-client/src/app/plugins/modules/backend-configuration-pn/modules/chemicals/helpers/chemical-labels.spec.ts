import {describe, it, expect} from 'vitest';
import {ChemicalLabelKind} from '../../../models';
import {entry, labelOf} from './chemical-inventory.fixtures';
import {labelOptionText, labelsOf} from './chemical-labels';

/** Echoes the key with its parameters filled in, like ngx-translate with the English keys. */
const translate = {
  instant: (key: string, params: Record<string, string> = {}) => key.replace(/{{(\w+)}}/g, (_m, name) => params[name]),
};
const label = labelOf;

describe('labelsOf', () => {
  it('lists the labels of an entry', () => {
    expect(labelsOf(entry(1, 'Karate', {labels: [label()]}))).toHaveLength(1);
  });

  it.each([['no entry', undefined], ['an entry from an older server', entry(1, 'Karate')], ['a null entry', null]])(
    'is empty for %s', (_name, source) => {
      expect(labelsOf(source)).toEqual([]);
    });
});

describe('labelOptionText', () => {
  it('names a packaged label by its packaging', () => {
    expect(labelOptionText(label({packaging: '5 L'}), translate)).toBe('Chemicals: Label (5 L)');
  });

  it('names a label without packaging plainly', () => {
    expect(labelOptionText(label(), translate)).toBe('Chemicals: Label');
  });

  it('names a minor-use document by its kind', () => {
    expect(labelOptionText(label({kind: ChemicalLabelKind.MinorUse}), translate)).toBe('Chemicals: Minor use');
  });

  it('shows the packaging of a minor-use document too, so two of them are told apart', () => {
    expect(labelOptionText(label({kind: ChemicalLabelKind.MinorUse, packaging: '5 L'}), translate)).toBe('Chemicals: Minor use (5 L)');
    expect(labelOptionText(label({kind: ChemicalLabelKind.MinorUse, packaging: '1 L'}), translate))
      .not.toBe(labelOptionText(label({kind: ChemicalLabelKind.MinorUse, packaging: '5 L'}), translate));
  });

  it('falls back to the revision when there is no revision date', () => {
    expect(labelOptionText(label({packaging: '5 L', revision: 'III'}), translate)).toBe('Chemicals: Label (5 L) · III');
    expect(labelOptionText(label({revision: 'III'}), translate)).toBe('Chemicals: Label · III');
  });

  it('prefers the revision date over the revision', () => {
    expect(labelOptionText(label({revisionDate: '2026-03-01T00:00:00Z', revision: 'III'}), translate)).toBe('Chemicals: Label · 01-03-2026');
  });

  it('appends the revision date as dd-MM-yyyy', () => {
    expect(labelOptionText(label({packaging: '5 L', revisionDate: '2026-03-01T00:00:00Z'}), translate))
      .toBe('Chemicals: Label (5 L) · 01-03-2026');
  });
});
