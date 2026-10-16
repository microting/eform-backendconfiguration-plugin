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

  // After a FAILED save the edits only stay in the form; the text must not say they were saved.
  const CLAIMS_SAVED: Record<string, RegExp> = {
    esES: /se han guardado/i, skSK: /zmeny sa uložili/i, bgBG: /са запазени/i, lvLV: /izmaiņas ir saglabātas/i,
    ltLT: /pakeitimai išsaugoti/i, hrHR: /promjene sačuvane|su promjene sačuvane/i, slSL: /spremembe so shranjene/i,
    ukUA: /зміни збережено/i, svSE: /ändringar sparas/i,
  };
  it.each(Object.entries(CLAIMS_SAVED))('%s: a failed save does not say the changes were saved', (locale, claim) => {
    expect(text(locale, 'Chemicals: Could not save settings')).not.toMatch(claim);
  });

  it.each(NON_ENGLISH)('%s: the "Push" column heading names push notifications, not pushing or "there"', (locale) => {
    expect(text(locale, 'Chemicals: Push')).toMatch(/push/i);
  });

  it.each(['esES', 'ptBR', 'ptPT'])('%s: the weekly-repeat hint says the product placement, not a location', (locale) => {
    expect(text(locale, 'Chemicals: Threshold passed hint')).not.toMatch(/ubicación|localização/i);
  });

  // A placement is a chemical registered at a location (W1), never a storage place, store room or warehouse
  // (Task 12 review I-3: "Chemikalienlager registrieren" read as "register a chemical warehouse").
  const STORAGE_PLACE = new RegExp([
    'lager', 'stockage', 'przechowywan', 'składowan', 'förvaring', 'opslag', 'зберіган', 'tároló', 'sklad',
    'almacenamiento', 'stoccaggio', 'armazenamento', 'lagring', 'säilytys', 'varasto', 'hoiukoh', 'ladustami',
    'uzglabāšan', 'saugojim', 'sandėliav', 'depozitar', 'съхранение', 'αποθήκευσ', 'geymslu',
  ].join('|'), 'i');
  const PLACEMENT_KEYS = ['Chemicals: Register placement', 'Chemicals: Action banner', 'Chemicals: Export removed placements'];

  it.each(NON_ENGLISH)('%s: W1 placement texts name registered chemicals, not storage places', (locale) => {
    const storage = PLACEMENT_KEYS.filter((key) => STORAGE_PLACE.test(text(locale, key)));
    expect(storage).toEqual([]);
  });

  // Moving or removing a placement acts on the registered chemical; read as a storage place, "Remove placement"
  // would sound like deleting the location itself.
  const PLACEMENT_ACTION_KEYS = ['Chemicals: Move placement', 'Chemicals: Remove placement'];

  it.each(NON_ENGLISH)('%s: moving and removing a placement name the chemical, not a storage place', (locale) => {
    const storage = PLACEMENT_ACTION_KEYS.filter((key) => STORAGE_PLACE.test(text(locale, key)));
    expect(storage).toEqual([]);
  });

  // A write-off is stock deducted from the inventory when a placement is removed, never accounting depreciation.
  const DEPRECIATION = /amorti|ammortam|deprecia|abschreib|afschrijv|avskriv|afskrift|amortyz|амортиз|αποσβ|poistot|értékcsökken/i;

  it.each(NON_ENGLISH)('%s: the removal write-off is a stock deduction, not depreciation', (locale) => {
    expect(text(locale, 'Chemicals: Stock origin write-off')).not.toMatch(DEPRECIATION);
  });

  // The stock balance is a quantity in stock, never an equilibrium or a bank/account balance.
  const NOT_QUANTITY = /gleichgewicht|équilibre|equilibrio|equilíbrio|evenwicht|jämvikt|likevekt|tasapaino|tasakaal|równowag|rovnováh|ravnotež|egyensúly|echilibru|pusiausvyr|līdzsvar|равновес|рівноваг|ισορροπ|jafnvægi|kontostand|solde|saldo bancario|saldo bancário|zůstatek|zostatok|egyenleg|баланс|guthaben/i;
  const BALANCE_KEYS = ['Chemicals: Balance', 'Chemicals: Current balance', 'Chemicals: Counted balance'];

  it.each(NON_ENGLISH)('%s: the stock balance is a quantity in stock, not an equilibrium or an account', (locale) => {
    const wrong = BALANCE_KEYS.filter((key) => NOT_QUANTITY.test(text(locale, key)));
    expect(wrong).toEqual([]);
  });

  // Moving a placement relocates a chemical between locations, never a person moving house
  // (Task 13: machine translation produced "Umziehen", "Me mudé", "Переїхав", "durch einen Zug").
  const RELOCATION = new RegExp([
    'umzug', 'umzieh', 'umgezogen', 'eingezogen', 'durch einen zug', 'verhuiz', 'verhuisd', 'déménag', 'emménag',
    'mudanza', 'me mud', 'mudarse', 'trasladarse', 'mudou-se', 'mudar-se', 'residência', 'trasferirsi', 'trasloc',
    'przeprowadz', 'переїзд', 'переїха', 'переезд', 'přestěh', 'odstěhov', 'presťah', 'preseli', 'muutt', 'muutto',
    'költöz', 'a se muta', 'mutați-vă', 'kolim', 'pārcēl', 'persikėl', 'išsikraust', 'преместих', 'μετακόμισ',
    'flutti', 'flyttelass', 'flyttlass',
  ].join('|'), 'i');
  const MOVE_KEYS = [
    'Chemicals: Move placement', 'Chemicals: Move', 'Chemicals: Move all', 'Chemicals: Move part',
    'Chemicals: Move to another location', 'Chemicals: Amount to move', 'Chemicals: Stock origin move',
    'Chemicals: Stock kind 4', 'Chemicals: Stock kind 5', 'Chemicals: Reason moved automatic', 'Chemicals: How much to move',
    'Chemicals: No other location',
  ];

  it.each(NON_ENGLISH)('%s: moving a placement relocates a chemical, not a person moving house', (locale) => {
    const relocation = MOVE_KEYS.filter((key) => RELOCATION.test(text(locale, key)));
    expect(relocation).toEqual([]);
  });

  it('deDE: a move is an Umlagerung, never "verschieben" (which also reads as postpone)', () => {
    expect(MOVE_KEYS.filter((key) => /verschieb/i.test(text('deDE', key)))).toEqual([]);
  });

  // Removing a placement keeps it in the history: the action is never "delete".
  const DELETE: Record<string, RegExp> = {esES: /eliminar|borrar/i, ukUA: /видал/i, deDE: /lösch/i, frFR: /supprim/i, itIT: /elimin|cancell/i};

  it.each(Object.entries(DELETE))('%s: removing a placement does not say delete', (locale, deleteWord) => {
    expect(['Chemicals: Remove', 'Chemicals: Remove placement'].filter((key) => deleteWord.test(text(locale, key)))).toEqual([]);
  });

  it.each(NON_ENGLISH)('%s: the reason "disposed of" is not the same word as the remove action', (locale) => {
    const stem = (key: string) => text(locale, key).toLocaleLowerCase().slice(0, 5);
    expect(stem('Chemicals: Reason disposed')).not.toBe(stem('Chemicals: Remove'));
  });

  // Hand-reviewed values the machine translation got wrong (Task 13 review M11).
  const REVIEWED: Array<[string, string, string]> = [
    ['elGR', 'Chemicals: Reason disposed', 'Διατέθηκε ως απόβλητο'],
    ['slSL', 'Chemicals: Reason disposed', 'Zavrženo kot odpadek'],
    ['svSE', 'Chemicals: Stock origin move', 'Flyttning'],
    ['isIS', 'Chemicals: Stock origin write-off', 'Birgðafrádráttur við fjarlægingu'],
    ['esES', 'Chemicals: Remove', 'Retirar'],
    ['ukUA', 'Chemicals: Remove', 'Вилучити'],
  ];

  it.each(REVIEWED)('%s keeps the reviewed %s', (locale, key, value) => {
    expect(text(locale, key)).toBe(value);
  });

  it('ptPT uses the European "stock", never the Brazilian "estoque", in every Kemi text', () => {
    expect(CHEMICAL_I18N_KEYS.filter((key) => /estoque/i.test(text('ptPT', key)))).toEqual([]);
  });
});
