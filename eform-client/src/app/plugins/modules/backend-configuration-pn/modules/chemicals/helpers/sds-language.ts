import {ChemicalSdsDocumentModel} from '../../../models';
import {ChemicalInventoryRow, sdsFileNameFor} from './chemical-inventory.helper';

/** The languages of the eForm UI (ISO 639-1); a locale outside this list has no SDS language. */
const KNOWN_LANGUAGES = new Set([
  'da', 'en', 'de', 'uk', 'pl', 'fr', 'es', 'it', 'nl', 'pt', 'no', 'sv', 'fi', 'et', 'lv', 'lt', 'ro', 'hr', 'sl', 'cs', 'sk', 'hu', 'bg', 'el', 'is',
]);

/** "DA" -> "da", "nb-NO" / "nn" -> "no", "pt-BR" -> "pt"; null, empty or unknown -> "". */
function languagePart(locale: string | null | undefined): string {
  const code = (locale ?? '').trim().toLowerCase().split(/[-_]/)[0];
  return code === 'nb' || code === 'nn' ? 'no' : code;
}

/** UI locale "da-DK" / "en-US" / "nb-NO" -> ISO 639-1 of a language the eForm UI has, else "". */
export function toIso6391(locale: string | null | undefined): string {
  const code = languagePart(locale);
  return KNOWN_LANGUAGES.has(code) ? code : '';
}

/** The SDS of the user's language; else Danish, then English, then the first one; undefined without documents. */
export function pickSds(docs: ChemicalSdsDocumentModel[] | null | undefined, uiLocale: string | null | undefined): ChemicalSdsDocumentModel | undefined {
  if (!docs?.length) {
    return undefined;
  }
  const inLanguage = (code: string) => (code ? docs.find((d) => languagePart(d.language) === code) : undefined);
  return inLanguage(toIso6391(uiLocale)) ?? inLanguage('da') ?? inLanguage('en') ?? docs[0];
}

/** The documents the "Andre sprog" menu lists: every one but the picked one, by language code. */
export function otherSdsDocuments(docs: ChemicalSdsDocumentModel[] | null | undefined, uiLocale: string | null | undefined): ChemicalSdsDocumentModel[] {
  const picked = pickSds(docs, uiLocale);
  return (docs ?? []).filter((d) => d !== picked).sort((a, b) => a.language.localeCompare(b.language));
}

/** What the SDS button opens: the document in the user's language, else (old data) the product's own sheet. */
export function sdsFileNameForLocale(row: ChemicalInventoryRow, uiLocale: string | null | undefined): string | null {
  return pickSds(row.entry?.sdsDocuments ?? [], uiLocale)?.fileName ?? sdsFileNameFor(row);
}

/** The i18n key of a language's name; a language without a key shows its upper-case code (see sdsLanguageText). */
export function sdsLanguageKey(code: string): string {
  return `Chemicals: Language ${languagePart(code)}`;
}

/** The translated name of a language, or its upper-case code when the UI has no name for it. */
export function sdsLanguageText(code: string, translate: {instant(key: string): string}): string {
  const key = sdsLanguageKey(code);
  const text = translate.instant(key);
  return text && text !== key ? text : (code ?? '').toUpperCase();
}

/** Stub: implemented after the red CI run. */
export function currentUiLocale(_translate: {getCurrentLang(): string | undefined; getFallbackLang(): string | null}): string {
  return undefined;
}
