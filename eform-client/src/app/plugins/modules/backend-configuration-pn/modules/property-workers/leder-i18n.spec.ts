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
import {LEDER_I18N_KEYS} from './leder-i18n-keys';

const LOCALES: Record<string, Record<string, unknown>> = {
  bgBG, csCZ, da, deDE, elGR, enUS, esES, etET, fiFI, frFR, hrHR, huHU, isIS,
  itIT, ltLT, lvLV, nlNL, noNO, plPL, ptBR, ptPT, roRO, skSK, slSL, svSE, ukUA,
};

describe('Leder i18n coverage', () => {
  it('checks all 26 locale files', () => {
    expect(Object.keys(LOCALES)).toHaveLength(26);
  });

  it.each(Object.keys(LOCALES))('%s has a non-empty value for every Leder key', locale => {
    const missing = LEDER_I18N_KEYS.filter(key => {
      const value = LOCALES[locale][key];
      return typeof value !== 'string' || value.trim() === '';
    });
    expect(missing).toEqual([]);
  });

  it('lists every key once', () => {
    expect(new Set(LEDER_I18N_KEYS).size).toBe(LEDER_I18N_KEYS.length);
  });

  it('English values equal their keys', () => {
    expect(LEDER_I18N_KEYS.filter(key => (enUS as Record<string, unknown>)[key] !== key)).toEqual([]);
  });

  it('Danish uses the contract texts', () => {
    const t = da as Record<string, unknown>;
    expect(t['Leader']).toBe('Leder');
    expect(t['Requires web or archive access']).toBe('Kræver web- eller arkivadgang');
    expect(t["Only an administrator can change a leader's login"]).toBe('Kun en administrator kan ændre en leders login');
    expect(t['Only administrators and leaders can do this']).toBe('Kun administratorer og ledere kan gøre det');
    expect(t['The move changes who can read the documents. Only an administrator or a leader can do that.'])
      .toBe('Flytningen ændrer, hvem der kan læse dokumenterne. Kun en administrator eller en leder kan gøre det.');
    expect(t['Save the worker with web or archive access first']).toBe('Gem først medarbejderen med web- eller arkivadgang');
  });
});
