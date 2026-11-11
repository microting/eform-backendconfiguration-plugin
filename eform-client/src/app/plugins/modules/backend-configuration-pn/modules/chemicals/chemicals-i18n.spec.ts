import {describe, it, expect} from 'vitest';
import {bgBG} from '../../i18n/bgBG';
import {csCZ} from '../../i18n/csCZ';
import {da} from '../../i18n/da';
import {deDE} from '../../i18n/deDE';
import {elGR} from '../../i18n/elGR';
import {enUS} from '../../i18n/enUS';
import {esES} from '../../i18n/esES';
import {etET} from '../../i18n/etET';
import {fiFI} from '../../i18n/fiFI';
import {frFR} from '../../i18n/frFR';
import {hrHR} from '../../i18n/hrHR';
import {huHU} from '../../i18n/huHU';
import {isIS} from '../../i18n/isIS';
import {itIT} from '../../i18n/itIT';
import {ltLT} from '../../i18n/ltLT';
import {lvLV} from '../../i18n/lvLV';
import {nlNL} from '../../i18n/nlNL';
import {noNO} from '../../i18n/noNO';
import {plPL} from '../../i18n/plPL';
import {ptBR} from '../../i18n/ptBR';
import {ptPT} from '../../i18n/ptPT';
import {roRO} from '../../i18n/roRO';
import {skSK} from '../../i18n/skSK';
import {slSL} from '../../i18n/slSL';
import {svSE} from '../../i18n/svSE';
import {ukUA} from '../../i18n/ukUA';
import {CHEMICAL_HISTORY_ACTIONS, HISTORY_TEXT_KEYS, historyActionKey} from './helpers/chemical-history.helper';
import {CHEMICAL_I18N_KEYS, CHEMICAL_REUSED_I18N_KEYS} from './chemicals-i18n-keys';

const LOCALES: Record<string, Record<string, unknown>> = {
  bgBG, csCZ, da, deDE, elGR, enUS, esES, etET, fiFI, frFR, hrHR, huHU, isIS,
  itIT, ltLT, lvLV, nlNL, noNO, plPL, ptBR, ptPT, roRO, skSK, slSL, svSE, ukUA,
};

describe('Kemi i18n coverage', () => {
  it('checks all 26 locale files', () => {
    expect(Object.keys(LOCALES)).toHaveLength(26);
  });

  it.each(Object.keys(LOCALES))('%s has a non-empty value for every Kemi key', (locale) => {
    const missing = CHEMICAL_I18N_KEYS.filter((key) => {
      const value = LOCALES[locale][key];
      return typeof value !== 'string' || value.trim() === '';
    });

    expect(missing).toEqual([]);
  });

  it('lists every key once', () => {
    expect(new Set(CHEMICAL_I18N_KEYS).size).toBe(CHEMICAL_I18N_KEYS.length);
  });

  it('prefixes every Kemi-only key', () => {
    const unprefixed = CHEMICAL_I18N_KEYS.filter(
      (key) => !CHEMICAL_REUSED_I18N_KEYS.includes(key) && !key.startsWith('Chemicals: '));

    expect(unprefixed).toEqual([]);
  });

  it('keeps Danish distinct from the key itself for Kemi-only keys', () => {
    // Reused keys are skipped: some are the same word in Danish ('Status').
    const raw = CHEMICAL_I18N_KEYS.filter((key) => !CHEMICAL_REUSED_I18N_KEYS.includes(key) && da[key] === key);

    expect(raw).toEqual([]);
  });

  it('lists every key the W6 history page builds at run time (action names, Excel detail words)', () => {
    const built = [...CHEMICAL_HISTORY_ACTIONS.map(historyActionKey), ...Object.values(HISTORY_TEXT_KEYS)];

    expect(built.filter((key) => !CHEMICAL_I18N_KEYS.includes(key))).toEqual([]);
  });

  const DOCUMENT_KEYS = CHEMICAL_I18N_KEYS.filter((key) => /^Chemicals: (Open label|Could not open label|Label|Minor use|Other languages|Language )/.test(key));

  it('lists the label and language keys of Task 15/15b (one name per language of the SDS menu)', () => {
    expect(DOCUMENT_KEYS).toHaveLength(31);
    expect(DOCUMENT_KEYS.filter((key) => key.startsWith('Chemicals: Language '))).toHaveLength(25);
  });

  it.each(Object.keys(LOCALES))('%s keeps the {{packaging}} parameter of the packaged label', (locale) => {
    expect(String(LOCALES[locale]['Chemicals: Label ({{packaging}})'])).toContain('{{packaging}}');
  });

  it.each(Object.keys(LOCALES).filter((locale) => locale !== 'enUS'))('%s translates the label and language keys (no English left)', (locale) => {
    const untranslated = DOCUMENT_KEYS.filter((key) => LOCALES[locale][key] === LOCALES['enUS'][key]);

    expect(untranslated).toEqual([]);
  });

  it('writes Danish as the owner specified', () => {
    expect(da['Chemicals: Open label']).toBe('Åbn etiket');
    expect(da['Chemicals: Minor use']).toBe('Mindre anvendelse');
    expect(da['Chemicals: Label ({{packaging}})']).toBe('Etiket ({{packaging}})');
    expect(da['Chemicals: Other languages']).toBe('Andre sprog');
  });
});
