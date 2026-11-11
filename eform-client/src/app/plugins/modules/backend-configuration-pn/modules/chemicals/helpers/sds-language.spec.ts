import {describe, it, expect} from 'vitest';
import {ChemicalSdsDocumentModel} from '../../../models';
import {entry, inventoryOf, location, placement} from './chemical-inventory.fixtures';
import {buildInventoryRows} from './chemical-inventory.helper';
import {otherSdsDocuments, pickSds, sdsFileNameForLocale, sdsLanguageKey, toIso6391} from './sds-language';

const doc = (language: string): ChemicalSdsDocumentModel => ({language, fileName: `md5${language}`, revisionDate: null, productName: ''});
const langs = (docs: ChemicalSdsDocumentModel[] | undefined) => docs?.map((d) => d.language);

/** The same vectors as the server's Normalize_cases (Task 1b) and the app's picker test (Task 19b). */
describe('toIso6391', () => {
  it.each([
    ['DA', 'da'],
    ['da', 'da'],
    ['da-DK', 'da'],
    ['en-US', 'en'],
    ['nb', 'no'],
    ['nb-NO', 'no'],
    ['nn', 'no'],
    ['no-NO', 'no'],
    ['pt-BR', 'pt'],
    ['pt-PT', 'pt'],
    ['sv-SE', 'sv'],
    ['xx', ''],
    ['', ''],
    [null, ''],
    [undefined, ''],
  ])('maps %j to %j', (locale, expected) => {
    expect(toIso6391(locale as string)).toBe(expected);
  });
});

describe('pickSds', () => {
  it('prefers the document in the user language (an exact match wins)', () => {
    expect(pickSds([doc('da'), doc('en'), doc('sv')], 'sv-SE')?.language).toBe('sv');
    expect(pickSds([doc('da'), doc('en')], 'en-US')?.fileName).toBe('md5en');
  });

  it('maps nb and nn to the Norwegian document', () => {
    expect(pickSds([doc('da'), doc('no')], 'nb-NO')?.language).toBe('no');
  });

  it('falls back to Danish when the user language is missing', () => {
    expect(pickSds([doc('da'), doc('en'), doc('sv')], 'de-DE')?.language).toBe('da');
  });

  it('falls back to English when neither the user language nor Danish exists', () => {
    expect(pickSds([doc('en'), doc('sv')], 'fr-FR')?.language).toBe('en');
  });

  it('falls back to the first document when there is no Danish or English one', () => {
    expect(pickSds([doc('sv')], 'fr-FR')?.language).toBe('sv');
    expect(pickSds([doc('sv'), doc('fi')], 'xx')?.language).toBe('sv');
  });

  it('does not crash on an unknown or empty locale', () => {
    expect(pickSds([doc('da'), doc('en')], 'xx')?.language).toBe('da');
    expect(pickSds([doc('da'), doc('en')], '')?.language).toBe('da');
    expect(pickSds([doc('da'), doc('en')], null as unknown as string)?.language).toBe('da');
  });

  it('returns undefined for an empty or missing list', () => {
    expect(pickSds([], 'da-DK')).toBeUndefined();
    expect(pickSds(undefined as unknown as ChemicalSdsDocumentModel[], 'da-DK')).toBeUndefined();
  });
});

describe('otherSdsDocuments', () => {
  it('lists every language but the picked one, sorted by language code', () => {
    expect(langs(otherSdsDocuments([doc('sv'), doc('da'), doc('en')], 'en-US'))).toEqual(['da', 'sv']);
  });

  it('excludes the fallback document that was picked', () => {
    expect(langs(otherSdsDocuments([doc('en'), doc('sv')], 'fr-FR'))).toEqual(['sv']);
  });

  it('is empty for one document, an empty list or a missing list', () => {
    expect(otherSdsDocuments([doc('da')], 'da-DK')).toEqual([]);
    expect(otherSdsDocuments([], 'da-DK')).toEqual([]);
    expect(otherSdsDocuments(undefined as unknown as ChemicalSdsDocumentModel[], 'da-DK')).toEqual([]);
  });
});

describe('sdsFileNameForLocale', () => {
  const rowOf = (partial: Parameters<typeof entry>[2]) =>
    buildInventoryRows(inventoryOf({
      locations: [location(10, 'Lade')], placements: [placement(1, 10, 200)], registerEntries: [entry(200, 'Karate', partial)],
    }), '2026-09-29')[0];

  it('opens the document in the user language', () => {
    const row = rowOf({sdsDocuments: [doc('da'), doc('en')]});

    expect(sdsFileNameForLocale(row, 'en-US')).toBe('md5en');
    expect(sdsFileNameForLocale(row, 'da-DK')).toBe('md5da');
  });

  it('falls back to products[].sdsFileName when the list is empty or missing (old data)', () => {
    expect(sdsFileNameForLocale(rowOf({sdsDocuments: []}), 'en-US')).toBe('sds200');
    expect(sdsFileNameForLocale(rowOf({sdsDocuments: undefined}), 'en-US')).toBe('sds200');
  });

  it('is null with neither a document nor a product sheet', () => {
    expect(sdsFileNameForLocale(rowOf({sdsDocuments: [], products: []}), 'en-US')).toBeNull();
  });
});

describe('sdsLanguageKey', () => {
  it('names the i18n key of a language', () => {
    expect(sdsLanguageKey('da')).toBe('Chemicals: Language da');
    expect(sdsLanguageKey('nb')).toBe('Chemicals: Language no');
  });
});
