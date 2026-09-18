import {TranslateService} from '@ngx-translate/core';
import {formatRepeatText} from './calendar-task-list-repeat.util';
import {CalendarRepeatService} from '../calendar/services/calendar-repeat.service';
import {CalendarTaskModel} from '../../models/calendar';

/**
 * #1289 — the task-list "Gentag" column for Nth-weekday-of-month rules.
 *
 * The customer's bug was in the DATA (a stale ordinal), not in this formatter,
 * but the formatter is what the admin reads to notice it, so every ordinal
 * 1..5 is pinned here — through both the repeatEvery = 1 ('monthlyDom') and
 * repeatEvery > 1 ('custom') mappings, in English and Danish. In particular
 * ordinal 1 goes through the monthlyFirstWeekday kind and 2..5 through
 * monthlyByDay, so both reconstruction branches are covered.
 */

// Interpolating stub: returns the key with {{params}} substituted, so the
// ordinal label that reaches the template is observable in the output.
function translateStub(lang: string): TranslateService {
  return {
    currentLang: lang,
    defaultLang: lang,
    instant: (key: string, params?: Record<string, unknown>) =>
      params ? key.replace(/{{\s*(\w+)\s*}}/g, (_m, p) => String(params[p])) : key,
  } as unknown as TranslateService;
}

function ordinalTask(repeatOrdinalWeek: number, repeatEvery = 1): CalendarTaskModel {
  return {
    id: 1,
    title: 't',
    repeatRule: repeatEvery === 1 ? 'monthlyDom' : 'custom',
    repeatType: 3,
    repeatEvery,
    repeatOrdinalWeek,
    dayOfWeek: 4, // Thursday (JS getDay)
    dayOfMonth: 0,
    taskDate: '2026-09-10',
  } as unknown as CalendarTaskModel;
}

describe('formatRepeatText — Nth weekday of month (#1289)', () => {
  describe('English', () => {
    const translate = translateStub('en');
    const service = new CalendarRepeatService(translate);

    it.each([
      [1, '1st'],
      [2, '2nd'],
      [3, '3rd'],
      [4, '4th'],
      [5, '5th'],
    ])('monthly, ordinal %i renders "%s"', (ordinal, label) => {
      const text = formatRepeatText(service, translate, ordinalTask(ordinal));
      expect(text.startsWith(`Monthly on the ${label} `)).toBe(true);
    });

    it.each([
      [1, '1st'],
      [2, '2nd'],
      [3, '3rd'],
      [4, '4th'],
      [5, '5th'],
    ])('every 2 months, ordinal %i renders "%s"', (ordinal, label) => {
      const text = formatRepeatText(service, translate, ordinalTask(ordinal, 2));
      expect(text.startsWith(`Every 2 months on the ${label} `)).toBe(true);
    });
  });

  describe('Danish', () => {
    const translate = translateStub('da');
    const service = new CalendarRepeatService(translate);

    it.each([1, 2, 3, 4, 5])('monthly, ordinal %i renders "%i."', ordinal => {
      const text = formatRepeatText(service, translate, ordinalTask(ordinal));
      expect(text.startsWith(`Monthly on the ${ordinal}. `)).toBe(true);
    });
  });

  it('the customer case: a "1st Monday" rule reads 1st, not the stale 2nd', () => {
    const translate = translateStub('en');
    const service = new CalendarRepeatService(translate);
    const task = {...ordinalTask(1), dayOfWeek: 1, taskDate: '2026-09-07'} as CalendarTaskModel;

    const text = formatRepeatText(service, translate, task);

    expect(text.startsWith('Monthly on the 1st ')).toBe(true);
    expect(text).not.toContain('2nd');
  });

  it('a non-repeating task still reads "Does not repeat"', () => {
    const translate = translateStub('en');
    const service = new CalendarRepeatService(translate);
    const task = {...ordinalTask(2), repeatRule: 'none'} as CalendarTaskModel;

    expect(formatRepeatText(service, translate, task)).toBe('Does not repeat');
  });
});
