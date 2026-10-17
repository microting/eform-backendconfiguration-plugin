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

  // German "umlagern"/"verlagern" (relocate stock, the fix-round-1 ruling for "move") contains "lager" but names no storage place.
  const RELOCATE_STOCK = /\b(um|ver)lager\w*/gi;

  it.each(NON_ENGLISH)('%s: moving and removing a placement name the chemical, not a storage place', (locale) => {
    const storage = PLACEMENT_ACTION_KEYS.filter((key) => STORAGE_PLACE.test(text(locale, key).replace(RELOCATE_STOCK, '')));
    expect(storage).toEqual([]);
  });

  // A write-off is stock deducted from the inventory when a placement is removed, never accounting depreciation.
  const DEPRECIATION = /amorti|ammortam|deprecia|abschreib|afschrijv|avskriv|afskrift|amortyz|амортиз|αποσβ|poistot|értékcsökken/i;

  it.each(NON_ENGLISH)('%s: the removal write-off is a stock deduction, not depreciation', (locale) => {
    expect(text(locale, 'Chemicals: Stock origin write-off')).not.toMatch(DEPRECIATION);
  });

  // The stock balance is a quantity in stock, never an equilibrium or a bank/account balance.
  const NOT_QUANTITY = /gleichgewicht|équilibre|equilibrio|equilíbrio|evenwicht|jämvikt|likevekt|tasapaino|tasakaal|równowag|rovnováh|ravnotež|egyensúly|echilibru|pusiausvyr|līdzsvar|равновес|рівноваг|ισορροπ|jafnvægi|kontostand|solde|saldo bancario|saldo bancário|zůstatek|zostatok|egyenleg|баланс|guthaben/i;
  const BALANCE_KEYS = ['Chemicals: Balance', 'Chemicals: Current balance', 'Chemicals: Counted balance', 'Chemicals: History balance'];

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
    // Task 15: the W6 action and the Excel's removal reason
    'Chemicals: Action Moved', 'Chemicals: History reason moved',
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
    expect(['Chemicals: Remove', 'Chemicals: Remove placement', 'Chemicals: Action Removed']
      .filter((key) => deleteWord.test(text(locale, key)))).toEqual([]);
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
    // Task 13 re-review N-m1: the write-off follows every removal (also "used up"), not only a disposal.
    ['roRO', 'Chemicals: Stock origin write-off', 'Scădere din stoc la îndepărtare'],
    // Task 14 review M8: reads right for a count of 1 too (da plural, et partitive), ro labels in the informal imperative.
    ['da', 'Chemicals: Archive blocked',
      'Lokationen kan først arkiveres, når alle placeringer her ({{count}}) er flyttet eller fjernet. Arkiverede lokationer beholder deres historik.'],
    ['etET', 'Chemicals: Archive blocked',
      'Asukoha saab arhiveerida, kui selle {{count}} registreeritud kemikaali on teise kohta teisaldatud või eemaldatud. Arhiveeritud asukohad säilitavad oma ajaloo.'],
    ['roRO', 'Chemicals: Edit location', 'Editează locația'],
  ];

  it.each(REVIEWED)('%s keeps the reviewed %s', (locale, key, value) => {
    expect(text(locale, key)).toBe(value);
  });

  // A location (W3) is the physical place where a chemical stands: never a warehouse or storage room, an internship
  // or job placement, or moving house (machine translation produced all three).
  const LOCATION_KEYS = [
    'Chemicals: Locations title', 'Chemicals: New location', 'Chemicals: Edit location', 'Chemicals: No locations',
    'Chemicals: Loading locations', 'Chemicals: Could not load locations', 'Chemicals: Archive blocked',
    'Chemicals: Archive location?', 'Chemicals: Archive confirm hint', 'Chemicals: Move location up', 'Chemicals: Move location down',
    // Task 15: the W6 location actions
    'Chemicals: Action LocationCreated', 'Chemicals: Action LocationRenamed', 'Chemicals: Action LocationArchived',
    'Chemicals: Action LocationPhotoChanged',
  ];
  const INTERNSHIP = new RegExp([
    '\\bstage\\b', 'stagiu', 'tirocin', 'praktikum', '\\bpraktik\\b', 'praktika', 'praktyk', 'staż', 'stáž', 'práctica',
    'estágio', 'harjoittel', 'starfsnám', 'πρακτική', 'стажув', 'стаж', 'prakse', 'praksa', 'gyakornok', 'szakmai gyakorlat',
  ].join('|'), 'i');

  it.each(NON_ENGLISH)('%s: a location is the place a chemical stands, not a storage place', (locale) => {
    expect(LOCATION_KEYS.filter((key) => STORAGE_PLACE.test(text(locale, key)))).toEqual([]);
  });

  it.each(NON_ENGLISH)('%s: a location is not an internship or a job placement', (locale) => {
    expect([...LOCATION_KEYS, 'Chemicals: Photo hint'].filter((key) => INTERNSHIP.test(text(locale, key)))).toEqual([]);
  });

  it.each(NON_ENGLISH)('%s: a location is not moving house', (locale) => {
    expect(LOCATION_KEYS.filter((key) => RELOCATION.test(text(locale, key)))).toEqual([]);
  });

  // "No." heads the order column: a number, never the word "no".
  const NEGATION: Record<string, RegExp> = {
    deDE: /^nein\.?$/i, esES: /^no\.?$/i, frFR: /^non\.?$/i, itIT: /^no\.?$/i, ptPT: /^não\.?$/i, ptBR: /^não\.?$/i,
    nlNL: /^nee\.?$/i, svSE: /^nej\.?$/i, noNO: /^nei\.?$/i, plPL: /^nie\.?$/i, ukUA: /^ні\.?$/i,
  };

  it.each(Object.entries(NEGATION))('%s: the order column "No." is a number, not "no"', (locale, negation) => {
    expect(text(locale, 'Chemicals: No.')).not.toMatch(negation);
  });

  // Archiving a location is an action (a verb on the button), never the noun "archive". Not anchored at the end:
  // machine translation produced "Archiwum tego" ("the archive of this").
  const ARCHIVE_NOUN: Record<string, RegExp> = {
    deDE: /^archiv\b/i, nlNL: /^archief\b/i, plPL: /^archiwum\b/i, frFR: /^archives?\b/i, esES: /^archivo\b/i,
    itIT: /^archivio\b/i, svSE: /^arkiv$/i, noNO: /^arkiv$/i, da: /^arkiv$/i, isIS: /^skjalasafn$/i,
  };

  it.each(Object.entries(ARCHIVE_NOUN))('%s: "Archive" is the action, not the noun', (locale, noun) => {
    expect(text(locale, 'Chemicals: Archive')).not.toMatch(noun);
  });

  it('the archive-noun guard catches what machine translation produced', () => {
    expect('Archiwum tego').toMatch(ARCHIVE_NOUN.plPL);
    expect('Archiv').toMatch(ARCHIVE_NOUN.deDE);
    expect('Archivieren').not.toMatch(ARCHIVE_NOUN.deDE);
  });

  // "Loading locations" is the screen loading data, never loading cargo (Task 14 machine translation produced
  // "Ladepositionen", "Ubicaciones de carga", "Emplacements de chargement", "Laadlocaties", "Locais de carregamento").
  const CARGO = /ladeposition|verlade|de carga|de chargement|laadlocat|de carregamento|ładunk|nakládk|lastning/i;

  it.each(NON_ENGLISH)('%s: loading locations is loading data, not cargo', (locale) => {
    expect(LOCATION_KEYS.filter((key) => CARGO.test(text(locale, key)))).toEqual([]);
  });

  it('the cargo guard catches what machine translation produced', () => {
    for (const bad of ['Ladepositionen…', 'Ubicaciones de carga…', 'Emplacements de chargement…', 'Laadlocaties…', 'Locais de carregamento…']) {
      expect(bad).toMatch(CARGO);
    }
  });

  it('ptPT uses the European "stock", never the Brazilian "estoque", in every Kemi text', () => {
    expect(CHEMICAL_I18N_KEYS.filter((key) => /estoque/i.test(text('ptPT', key)))).toEqual([]);
  });

  // Task 15 - W6 "Historik" is the property's event log (browser-history sense), never history the subject or a story.
  const HISTORY_KEYS = ['Chemicals: History title', 'Chemicals: Loading history', 'Chemicals: Could not load history'];
  const HISTORY_SUBJECT: Record<string, RegExp> = {
    deDE: /geschichte/i, frFR: /\bhistoire\b/i, itIT: /\bstoria\b/i, esES: /\bhistoria\b/i, ptBR: /\bhistória\b/i, ptPT: /\bhistória\b/i,
    roRO: /\bistorie\b/i, huHU: /történe/i, elGR: /ιστορία/i, isIS: /\bsaga\b/i, nlNL: /geschiedenis/i,
  };

  it.each(Object.entries(HISTORY_SUBJECT))('%s: the history page is an event log, not history the subject or a story', (locale, subject) => {
    expect(HISTORY_KEYS.filter((key) => subject.test(text(locale, key)))).toEqual([]);
  });

  it('the history-subject guard catches what machine translation produced', () => {
    expect('Geschichte').toMatch(HISTORY_SUBJECT.deDE);
    expect('Storia').toMatch(HISTORY_SUBJECT.itIT);
    expect('Historia').toMatch(HISTORY_SUBJECT.esES);
    expect('Saga').toMatch(HISTORY_SUBJECT.isIS);
    expect('Történet').toMatch(HISTORY_SUBJECT.huHU);
    expect('Historial').not.toMatch(HISTORY_SUBJECT.esES);
  });

  // "Action" heads the event-type column and "Stock" is goods in stock: never a share on the stock market
  // (machine translation produced "Aktie", "Hlutabréf", "Aktienkurs", "cours de l'action").
  const SHARE = /aktie|hlutabr|osake|aktsia|акці[яї]|акци[яи]|μετοχ|cours de l/i;
  const SHARE_KEYS = [
    'Chemicals: Action', 'Chemicals: All actions', 'Chemicals: Flag stock', 'Chemicals: Action StockReceived',
    'Chemicals: Action StockConsumed', 'Chemicals: Action StockAdjusted', 'Chemicals: Stock turned on', 'Chemicals: Stock turned off',
    'Chemicals: History balance',
  ];

  it.each(NON_ENGLISH)('%s: an action is an event and stock is goods, never a company share', (locale) => {
    expect(SHARE_KEYS.filter((key) => SHARE.test(text(locale, key)))).toEqual([]);
  });

  // A moved chemical is relocated, never emotionally moved (machine translation produced "Emocionado", "Wzruszony", "Rörd").
  const EMOTION = /emocion|wzrusz|^rörd|commoss|gerührt|ontroer|(^|\s)ému|dojat|liikutt/i;

  it.each(NON_ENGLISH)('%s: "Moved" relocates a chemical, it is not an emotion', (locale) => {
    expect(['Chemicals: Action Moved', 'Chemicals: History reason moved'].filter((key) => EMOTION.test(text(locale, key)))).toEqual([]);
  });

  it('the share and emotion guards catch what machine translation produced', () => {
    for (const bad of ['Aktie', 'Hlutabréf', 'Aktienkurs korrigierte sich', 'Le cours de l\'action a été corrigé']) {
      expect(bad).toMatch(SHARE);
    }
    for (const bad of ['Emocionado', 'Wzruszony', 'Rörd']) {
      expect(bad).toMatch(EMOTION);
    }
    expect('Umgelagert').not.toMatch(EMOTION);
    expect('Aktion').not.toMatch(SHARE);
  });

  // "Consumed" is stock used up, never digested or consummated (machine translation produced "Strawiony", "Consumado").
  it.each(NON_ENGLISH)('%s: consumed stock is used up, not digested or consummated', (locale) => {
    expect(text(locale, 'Chemicals: Action StockConsumed')).not.toMatch(/strawi|consumad|geconsumeerd/i);
  });

  // The W6 action names are labels, never shouted (machine translation produced "ENTFERNT", "REMOVED", "RIMOSSO").
  const ACTION_KEYS = Object.keys(enUS).filter((key) => key.startsWith('Chemicals: Action ') && key !== 'Chemicals: Action banner');

  it('has one action name per history action', () => {
    expect(ACTION_KEYS).toHaveLength(15);
  });

  it.each(Object.keys(LOCALES))('%s writes the action names in normal case', (locale) => {
    expect(ACTION_KEYS.filter((key) => text(locale, key) === text(locale, key).toLocaleUpperCase())).toEqual([]);
  });

  // The placement note is a note about the spot, never an internship ("Note de stage").
  it.each(NON_ENGLISH)('%s: the placement-note action is not an internship', (locale) => {
    expect(text(locale, 'Chemicals: Action NoteChanged')).not.toMatch(INTERNSHIP);
  });

  // An archived location is put in the archive, not stored (Icelandic "geymd" reads as kept in storage, Task 14).
  it('isIS: an archived location is put in the archive, not stored', () => {
    expect(text('isIS', 'Chemicals: Action LocationArchived')).not.toMatch(/geym/i);
  });

  // The upper date bound is "until", never a direction or a purpose ("zu", "à", "para").
  const TO_DIRECTION: Record<string, RegExp> = {
    deDE: /^(zu|nach|an)$/i, frFR: /^(à|vers)$/i, esES: /^(a|para)$/i, ptBR: /^(para|a)$/i, ptPT: /^(para|a)$/i,
    itIT: /^(a|per)$/i, nlNL: /^(naar|aan)$/i,
  };

  it.each(Object.entries(TO_DIRECTION))('%s: "To" ends a period, it is not a direction', (locale, direction) => {
    expect(text(locale, 'Chemicals: To')).not.toMatch(direction);
  });

  // The time column is a timestamp, never the weather ("Tiempo", "Tempo", "Temps").
  it.each(['esES', 'ptBR', 'ptPT', 'itIT', 'frFR'])('%s: the time column is a date and time, not the weather', (locale) => {
    expect(text(locale, 'Chemicals: Time')).not.toMatch(/^(tiempo|tempo|temps)$/i);
  });

  // The removal reason label is a noun, never the verb "to reason" (sv "Resonera").
  it('svSE: the removal reason label is a noun', () => {
    expect(text('svSE', 'Chemicals: History reason')).not.toMatch(/^resonera/i);
  });

  // A threshold names a deadline coming up, never a delivery or submission date.
  const DELIVERY = /entrega|inlever|skilafrest|realizacji|виконання/i;

  it.each(NON_ENGLISH)('%s: an alert threshold is a deadline, not a delivery or submission date', (locale) => {
    expect(['Chemicals: History threshold six months', 'Chemicals: History threshold one month']
      .filter((key) => DELIVERY.test(text(locale, key)))).toEqual([]);
  });

  // Hand-reviewed W6 values (Task 15).
  const REVIEWED_HISTORY: Array<[string, string, string]> = [
    ['da', 'Chemicals: History title', 'Historik'],
    ['deDE', 'Chemicals: History title', 'Verlauf'],
    ['frFR', 'Chemicals: History title', 'Historique'],
    ['esES', 'Chemicals: History title', 'Historial'],
    ['itIT', 'Chemicals: History title', 'Cronologia'],
    ['ptPT', 'Chemicals: History title', 'Histórico'],
    ['deDE', 'Chemicals: Action Moved', 'Umgelagert'],
    ['esES', 'Chemicals: Action Moved', 'Trasladado'],
    ['svSE', 'Chemicals: History reason', 'Orsak'],
    ['deDE', 'Chemicals: To', 'Bis'],
    // The Excel's own words (localization.json) in the two languages the customers read.
    ['da', 'Chemicals: History reason', 'Årsag'],
    ['da', 'Chemicals: History balance', 'saldo'],
    ['enUS', 'Chemicals: History balance', 'balance'],
    ['da', 'Chemicals: History digest recipients', 'Modtagere af oversigt'],
    ['da', 'Chemicals: History threshold six months', 'Frist inden for 6 måneder'],
    ['da', 'Chemicals: Action NoteChanged', 'Placeringsnote rettet'],
  ];

  it.each(REVIEWED_HISTORY)('%s keeps the reviewed %s', (locale, key, value) => {
    expect(text(locale, key)).toBe(value);
  });

  // Task 15 review m-6: the permission flags now also appear in the W6 details ("+Registrere"), so they are labels of a
  // right: a capitalised word, never a visit (es "Visita"), viewing a film (fr "visionnage") or an adjective (pl).
  const FLAG_KEYS = ['Chemicals: Flag view', 'Chemicals: Flag register', 'Chemicals: Flag remove', 'Chemicals: Flag stock',
    'Chemicals: Flag manage locations', 'Chemicals: Flag admin'];
  const NOT_A_RIGHT = /visita|visionnage|rejestracyjn|^spis$/i;

  it.each(Object.keys(LOCALES))('%s writes the permission flags as capitalised labels', (locale) => {
    expect(FLAG_KEYS.filter((key) => text(locale, key)[0] !== text(locale, key)[0].toLocaleUpperCase())).toEqual([]);
  });

  it.each(NON_ENGLISH)('%s: a permission flag names a right, not a visit, a screening or an adjective', (locale) => {
    expect(FLAG_KEYS.filter((key) => NOT_A_RIGHT.test(text(locale, key)))).toEqual([]);
  });

  it('the flag guard catches what machine translation produced', () => {
    for (const bad of ['Visita', 'visionnage', 'Rejestracyjny', 'Spis']) {
      expect(bad).toMatch(NOT_A_RIGHT);
    }
    expect('Rejestracja').not.toMatch(NOT_A_RIGHT);
  });

  const REVIEWED_FIX_ROUND_1: Array<[string, string, string]> = [
    ['esES', 'Chemicals: Flag view', 'Ver'],
    ['frFR', 'Chemicals: Flag view', 'Consultation'],
    ['plPL', 'Chemicals: Flag register', 'Rejestracja'],
    ['plPL', 'Chemicals: Flag stock', 'Magazyn'],
    // m-10: the action names agree with "Flyttad"/"Borttagen" and "Przeniesiono"/"Usunięto".
    ['svSE', 'Chemicals: Action StockAdjusted', 'Justerad'],
    ['plPL', 'Chemicals: Action StockConsumed', 'Zużyto'],
    ['da', 'Chemicals: Archived', 'arkiveret'],
  ];

  it.each(REVIEWED_FIX_ROUND_1)('%s keeps the reviewed %s', (locale, key, value) => {
    expect(text(locale, key)).toBe(value);
  });
});
