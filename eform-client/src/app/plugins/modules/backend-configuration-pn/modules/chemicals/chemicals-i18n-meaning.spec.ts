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
import {CHEMICAL_I18N_KEYS, CHEMICAL_REUSED_I18N_KEYS} from './chemicals-i18n-keys';

const LOCALES: Record<string, Record<string, unknown>> = {
  bgBG, csCZ, da, deDE, elGR, enUS, esES, etET, fiFI, frFR, hrHR, huHU, isIS,
  itIT, ltLT, lvLV, nlNL, noNO, plPL, ptBR, ptPT, roRO, skSK, slSL, svSE, ukUA,
};
const NON_ENGLISH = Object.keys(LOCALES).filter((locale) => locale !== 'enUS');
const text = (locale: string, key: string) => String(LOCALES[locale][key]);

/**
 * Meaning of the Kemi strings (Copilot review on PR 1434): an off switch is a switched-off setting, never
 * a disabled person; BMD is the register's name, never bone density; the weekly-repeat hint is about a
 * chemical placement and its warning, never a medical dressing or implant.
 */
describe('Kemi i18n meaning', () => {
  const OFF_STATE: Record<string, string> = {
    ukUA: 'Вимкнено', svSE: 'Inaktiverad', roRO: 'Dezactivat', noNO: 'Deaktivert', nlNL: 'Uitgeschakeld',
    lvLV: 'Atspējots', ltLT: 'Išjungta', itIT: 'Disabilitato', huHU: 'Letiltva', fiFI: 'Ei käytössä',
    etET: 'Välja lülitatud', elGR: 'Απενεργοποιημένο', bgBG: 'Деактивирано',
  };
  const DISABILITY = /инвалід|увреждан|ανάπηρ|puudega|liikuntarajoit|neįgal|invalīd|funksjonshem|funktionshind|gehandicapt|\bdisabile\b|dizabilit|mozgáskorlát|handicap|behindert/i;
  const MEDICAL = /curativo|pleister|implant|beültet|wszczep|bandage|dressing/i;
  const REPAIRED = /opraveno|opravené|behoben|διορθ|pataisyta|lagfært|parandatud|solucionado|corrigido|fikset|javítva|åtgärdat|виправлено|naprawiono|korjattu|reparat|prijs/i;

  it.each(Object.entries(OFF_STATE))('%s: "Chemicals: Disabled" is the exact off-state label', (locale, expected) => {
    expect(text(locale, 'Chemicals: Disabled')).toBe(expected);
  });

  it.each(NON_ENGLISH)('%s: "Chemicals: Disabled" never names a disabled person', (locale) => {
    expect(text(locale, 'Chemicals: Disabled')).not.toMatch(DISABILITY);
  });

  const bmdKeys = Object.keys(enUS).filter((key) => /\bBMD\b/.test(String((enUS as Record<string, unknown>)[key])));

  it('has at least one English string that names BMD', () => {
    expect(bmdKeys).toContain('Chemicals: Threshold status changed hint');
  });

  it.each(NON_ENGLISH)('%s keeps the literal "BMD" wherever the English text has it', (locale) => {
    const lost = bmdKeys.filter((key) => !text(locale, key).includes('BMD'));
    expect(lost).toEqual([]);
  });

  it.each(NON_ENGLISH)('%s: the weekly-repeat hint is about the placement, not a medical item', (locale) => {
    expect(text(locale, 'Chemicals: Threshold passed hint')).not.toMatch(MEDICAL);
  });

  it.each(NON_ENGLISH)('%s: "Fixed for all customers" does not read as repaired or priced', (locale) => {
    expect(text(locale, 'Chemicals: Fixed for all customers')).not.toMatch(REPAIRED);
  });
});
