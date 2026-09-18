import {TranslateService} from '@ngx-translate/core';
import {
  CalendarRepeatService,
  parseDateOnlyToLocalMidnight,
  toDateOnlyString,
} from './calendar-repeat.service';
import {CalendarRepeatMeta, CalendarTaskModel} from '../../../models/calendar';
import {formatRepeatText} from '../../calendar-task-list/calendar-task-list-repeat.util';
import {da} from '../../../i18n/da';
import {enUS} from '../../../i18n/enUS';
import {deDE} from '../../../i18n/deDE';
import {useFixedUtcOffset} from './fixed-offset-date.testing';

/**
 * #1293 — the Gentag label shows the custom repeat's END condition.
 *
 * Customer: "Når der trykkes OK, skal der i Gentag vises: Ugentlig hver
 * torsdag, til og med 10. december 2026" / "…, 10 gange". Product decision:
 * keep the existing rule wording ("Ugentligt hver …") and only append the
 * suffix, for every kind.
 *
 * The translate stub looks keys up in the plugin's REAL da / enUS / deDE
 * dictionaries and interpolates {{params}}, so a missing or mistranslated key
 * (or a translated placeholder name) fails here.
 */

type Dict = Record<string, string>;

function translateFor(lang: 'da' | 'en' | 'de'): TranslateService {
  const dict: Dict = (lang === 'da' ? da : lang === 'de' ? deDE : enUS) as unknown as Dict;
  return {
    currentLang: lang,
    defaultLang: lang,
    instant: (key: string, params?: Record<string, unknown>) => {
      const template = dict[key] ?? key;
      return params
        ? template.replace(/{{\s*(\w+)\s*}}/g, (_m, p) => String(params[p]))
        : template;
    },
  } as unknown as TranslateService;
}

const LOCALE_OF = {da: 'da-DK', en: 'en-GB', de: 'de-DE'} as const;

// Local midnight of Thursday 10 December 2026 — what the custom-repeat
// modal's date picker hands back.
const UNTIL_10_DEC = new Date(2026, 11, 10).getTime();

const EXPECTED_SUFFIX = {
  da: {until: ', til og med 10. december 2026', after1: ', 1 gang', after10: ', 10 gange'},
  en: {until: ', until and including 10 December 2026', after1: ', 1 time', after10: ', 10 times'},
  de: {until: ', bis einschließlich 10. Dezember 2026', after1: ', 1 Mal', after10: ', 10 Mal'},
} as const;

// Every kind the product decision lists. weekday 4 = Thursday (JS getDay()).
const KINDS: [string, Omit<CalendarRepeatMeta, 'endMode'>][] = [
  ['daily', {kind: 'daily', n: 1}],
  ['every N days', {kind: 'everyNd', n: 3}],
  ['weekly', {kind: 'weeklyOne', n: 1, weekday: 4}],
  ['monthly day-of-month', {kind: 'monthlyDom', n: 1, dom: 10}],
  ['monthly Nth weekday', {kind: 'monthlyByDay', n: 1, ordinalWeek: 2, weekday: 4}],
  ['every N months', {kind: 'everyNMonthDom', n: 2, dom: 10}],
  ['yearly', {kind: 'yearlyOne', n: 1, dom: 10, month: 11}],
];

const LANGS = ['da', 'en', 'de'] as const;

// Cartesian product kind × locale, one row per cell.
const CELLS = KINDS.flatMap(([name, meta]) =>
  LANGS.map(lang => [name, lang, meta] as [string, typeof LANGS[number], Omit<CalendarRepeatMeta, 'endMode'>]));

describe('formatCustomRepeatLabel — end condition suffix (#1293)', () => {
  function label(lang: typeof LANGS[number], meta: CalendarRepeatMeta): string {
    const service = new CalendarRepeatService(translateFor(lang));
    return service.formatCustomRepeatLabel(meta, LOCALE_OF[lang]);
  }

  describe('end: never — the bare rule, no suffix', () => {
    it.each(CELLS)('%s (%s)', (_name, lang, meta) => {
      const text = label(lang, {...meta, endMode: 'never'} as CalendarRepeatMeta);
      const s = EXPECTED_SUFFIX[lang];
      expect(text).not.toContain(s.until);
      expect(text.endsWith(s.after10)).toBe(false);
      expect(text.endsWith(s.after1)).toBe(false);
      // The rule itself was translated (no raw key leaked through).
      expect(text).not.toContain('{{');
    });
  });

  describe('end: after 1 — singular', () => {
    it.each(CELLS)('%s (%s)', (_name, lang, meta) => {
      const rule = label(lang, {...meta, endMode: 'never'} as CalendarRepeatMeta);
      const text = label(lang, {...meta, endMode: 'after', afterCount: 1} as CalendarRepeatMeta);
      expect(text).toBe(rule + EXPECTED_SUFFIX[lang].after1);
    });
  });

  describe('end: after 10 — plural', () => {
    it.each(CELLS)('%s (%s)', (_name, lang, meta) => {
      const rule = label(lang, {...meta, endMode: 'never'} as CalendarRepeatMeta);
      const text = label(lang, {...meta, endMode: 'after', afterCount: 10} as CalendarRepeatMeta);
      expect(text).toBe(rule + EXPECTED_SUFFIX[lang].after10);
    });
  });

  describe('end: until 10 Dec 2026', () => {
    it.each(CELLS)('%s (%s)', (_name, lang, meta) => {
      const rule = label(lang, {...meta, endMode: 'never'} as CalendarRepeatMeta);
      const text = label(lang, {...meta, endMode: 'until', untilTs: UNTIL_10_DEC} as CalendarRepeatMeta);
      expect(text).toBe(rule + EXPECTED_SUFFIX[lang].until);
    });
  });

  describe('the customer\'s exact strings (da)', () => {
    const thursday: CalendarRepeatMeta = {kind: 'weeklyOne', n: 1, weekday: 4, endMode: 'never'};

    it('until: "Ugentligt hver torsdag, til og med 10. december 2026"', () => {
      expect(label('da', {...thursday, endMode: 'until', untilTs: UNTIL_10_DEC}))
        .toBe('Ugentligt hver torsdag, til og med 10. december 2026');
    });

    it('after 10: "Ugentligt hver torsdag, 10 gange"', () => {
      expect(label('da', {...thursday, endMode: 'after', afterCount: 10}))
        .toBe('Ugentligt hver torsdag, 10 gange');
    });

    it('after 1: "Ugentligt hver torsdag, 1 gang" (not "1 gange")', () => {
      expect(label('da', {...thursday, endMode: 'after', afterCount: 1}))
        .toBe('Ugentligt hver torsdag, 1 gang');
    });
  });

  it('feeds the Gentag dropdown\'s customCurrent option', () => {
    const service = new CalendarRepeatService(translateFor('da'));
    const meta: CalendarRepeatMeta = {kind: 'weeklyOne', n: 1, weekday: 4, endMode: 'after', afterCount: 10};
    const options = service.buildRepeatSelectOptions(new Date(2026, 8, 10), meta);
    expect(options.find(o => o.value === 'customCurrent')?.label).toBe('Ugentligt hver torsdag, 10 gange');
  });
});

// ─── Round trip across browser time zones ────────────────────────────────────

function savedTask(repeatUntilDate: string): CalendarTaskModel {
  return {
    id: 1, title: 't', startHour: 9, duration: 1, startText: '09:00', endText: '10:00',
    tags: [], assigneeIds: [], boardId: 1, color: '', descriptionHtml: '',
    repeatRule: 'weeklyOne', taskDate: '2026-11-19', completed: false, propertyId: 1,
    repeatType: 2, repeatEvery: 1, repeatEndMode: 2, repeatOccurrences: null,
    repeatUntilDate, dayOfWeek: 4, dayOfMonth: 0, repeatWeekdaysCsv: null,
  } as unknown as CalendarTaskModel;
}

/**
 * Save "until 10 Dec" → wire payload → backend echo → reload → label + last
 * occurrence, with the browser in UTC+1 (Copenhagen winter), UTC+2
 * (Copenhagen summer, DST) and UTC−5 (New York). `process.env.TZ` does not
 * reach Node from inside a jest test, so the zone is simulated with
 * `useFixedUtcOffset` (see fixed-offset-date.testing.ts).
 */
describe('repeat-until round trip across browser time zones (#1293)', () => {
  let restore: (() => void) | null = null;

  function inZone(offsetHours: number) {
    restore = useFixedUtcOffset(offsetHours);
  }

  afterEach(() => {
    restore?.();
    restore = null;
  });

  // [UTC offset, month index, day, expected wire date, expected da label date]
  const ZONES: [number, number, number, string, string][] = [
    [1, 11, 10, '2026-12-10', '10. december 2026'],   // Copenhagen winter
    [2, 5, 11, '2026-06-11', '11. juni 2026'],         // Copenhagen summer (DST)
    [-5, 11, 10, '2026-12-10', '10. december 2026'],  // New York
  ];

  it('the zone shim reproduces the original bug: UTC+1 local midnight 10 Dec is 2026-12-09T23:00Z', () => {
    inZone(1);
    expect(new Date(2026, 11, 10).toISOString()).toBe('2026-12-09T23:00:00.000Z');
  });

  it.each(ZONES)('UTC%s, %s/%s: the payload is date-only %s', (offset, month, day, wire) => {
    inZone(offset);
    const untilTs = new Date(2026, month, day).getTime();
    // The old payload was `new Date(untilTs).toISOString()` — the previous
    // day at 23:00/22:00Z east of UTC.
    expect(toDateOnlyString(untilTs)).toBe(wire);
  });

  it.each(ZONES)('UTC%s, %s/%s: reload of "%sT00:00:00" labels "%s"', (offset, month, day, wire, daDate) => {
    inZone(offset);
    const translate = translateFor('da');
    const service = new CalendarRepeatService(translate);

    // The backend echoes a Kind=Unspecified midnight: "yyyy-MM-ddT00:00:00".
    const task = savedTask(`${wire}T00:00:00`);
    const meta = service.reconstructMetaFromTask(task)!;

    expect(meta.untilTs).toBe(new Date(2026, month, day).getTime());
    expect(formatRepeatText(service, translate, task)).toBe(`Ugentligt hver torsdag, til og med ${daDate}`);
  });

  it.each(ZONES)('UTC%s, %s/%s: a "%sT00:00:00Z" echo still reads as that day', (offset, month, day, wire) => {
    inZone(offset);
    const service = new CalendarRepeatService(translateFor('da'));
    // new Date('2026-12-10T00:00:00Z') is 9 Dec 19:00 in UTC−5 — the day must
    // come from the Y-M-D, not from the instant.
    const meta = service.reconstructMetaFromTask(savedTask(`${wire}T00:00:00Z`))!;
    expect(meta.untilTs).toBe(new Date(2026, month, day).getTime());
  });

  it.each([[1], [2], [-5]])('UTC%s: the reloaded rule still renders its 10 Dec occurrence (inclusive)', offset => {
    inZone(offset);
    const service = new CalendarRepeatService(translateFor('da'));
    const meta = service.reconstructMetaFromTask(savedTask('2026-12-10T00:00:00'))!;
    const occurrences = service.getAllOccurrences(meta, new Date(2026, 10, 19).getTime());
    expect(occurrences[occurrences.length - 1]).toBe(new Date(2026, 11, 10).getTime());
  });
});

describe('parseDateOnlyToLocalMidnight / toDateOnlyString (#1293)', () => {
  it('reads the Y-M-D prefix as local midnight', () => {
    expect(parseDateOnlyToLocalMidnight('2026-12-10')).toBe(new Date(2026, 11, 10).getTime());
    expect(parseDateOnlyToLocalMidnight('2026-12-10T00:00:00')).toBe(new Date(2026, 11, 10).getTime());
  });

  it('31 Dec → 1 Jan boundary', () => {
    expect(toDateOnlyString(new Date(2026, 11, 31))).toBe('2026-12-31');
    expect(toDateOnlyString(new Date(2027, 0, 1))).toBe('2027-01-01');
    expect(parseDateOnlyToLocalMidnight('2027-01-01T00:00:00')).toBe(new Date(2027, 0, 1).getTime());
  });

  it('falls back to Date parsing for a non-Y-M-D value', () => {
    expect(parseDateOnlyToLocalMidnight('Thu, 10 Dec 2026 00:00:00 GMT'))
      .toBe(new Date('Thu, 10 Dec 2026 00:00:00 GMT').getTime());
  });
});
