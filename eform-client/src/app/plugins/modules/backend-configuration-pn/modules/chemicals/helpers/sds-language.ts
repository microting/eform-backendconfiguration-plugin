import {ChemicalSdsDocumentModel} from '../../../models';
import {ChemicalInventoryRow} from './chemical-inventory.helper';

/** Stub: implemented after the red CI run. */
export function toIso6391(_locale: string): string {
  return undefined;
}

export function pickSds(_docs: ChemicalSdsDocumentModel[], _uiLocale: string): ChemicalSdsDocumentModel | undefined {
  return null;
}

export function otherSdsDocuments(_docs: ChemicalSdsDocumentModel[], _uiLocale: string): ChemicalSdsDocumentModel[] {
  return undefined;
}

export function sdsFileNameForLocale(_row: ChemicalInventoryRow, _uiLocale: string): string | null {
  return undefined;
}

export function sdsLanguageKey(_code: string): string {
  return undefined;
}
