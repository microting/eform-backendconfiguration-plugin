import {CalendarRepeatService} from './calendar-repeat.service';
import {CalendarRepeatMeta, CalendarTaskModel} from '../../../models/calendar';
import {TranslateService} from '@ngx-translate/core';

// Monday 2026-03-16 00:00:00 UTC
const BASE_DATE = new Date('2026-03-16T00:00:00').getTime();

// Pass-through stub: `instant(key)` returns the key itself, which is the same
// fallback ngx-translate uses when a key is missing. Sufficient for tests that
// only inspect option shape, not localised strings.
const translateStub = {
  instant: (key: string, _params?: any) => key,
} as unknown as TranslateService;

function dayOf(ts: number): number {
  return new Date(ts).getDate();
}

function weekdayOf(ts: number): number {
  return new Date(ts).getDay();
}

describe('CalendarRepeatService', () => {
  let service: CalendarRepeatService;

  beforeEach(() => {
    service = new CalendarRepeatService(translateStub);
  });

  // ─── getAllOccurrences ─────────────────────────────────────────────────────

  describe('getAllOccurrences – daily', () => {
    it('returns exactly afterCount consecutive days', () => {
      const meta: CalendarRepeatMeta = {kind: 'daily', endMode: 'after', afterCount: 5};
      const result = service.getAllOccurrences(meta, BASE_DATE);
      expect(result).toHaveLength(5);
    });

    it('consecutive days are exactly 1 day apart', () => {
      const meta: CalendarRepeatMeta = {kind: 'daily', endMode: 'after', afterCount: 3};
      const result = service.getAllOccurrences(meta, BASE_DATE);
      expect((result[1] - result[0]) / 86400000).toBe(1);
      expect((result[2] - result[1]) / 86400000).toBe(1);
    });

    it('endMode: until filters to dates on or before cutoff', () => {
      const until = new Date('2026-03-20').getTime();
      const meta: CalendarRepeatMeta = {kind: 'daily', endMode: 'until', untilTs: until};
      const result = service.getAllOccurrences(meta, BASE_DATE);
      // 16, 17, 18, 19, 20 → 5 days
      expect(result).toHaveLength(5);
      expect(new Date(result[result.length - 1]).getDate()).toBe(20);
    });

    it('endMode: never returns up to 250 results', () => {
      const meta: CalendarRepeatMeta = {kind: 'daily', endMode: 'never'};
      const result = service.getAllOccurrences(meta, BASE_DATE);
      expect(result.length).toBeLessThanOrEqual(250);
      expect(result.length).toBeGreaterThan(0);
    });
  });

  describe('getAllOccurrences – everyNd', () => {
    it('every 3 days with after=4 returns 4 dates each 3 days apart', () => {
      const meta: CalendarRepeatMeta = {kind: 'everyNd', n: 3, endMode: 'after', afterCount: 4};
      const result = service.getAllOccurrences(meta, BASE_DATE);
      expect(result).toHaveLength(4);
      expect((result[1] - result[0]) / 86400000).toBe(3);
      expect((result[2] - result[1]) / 86400000).toBe(3);
    });
  });

  describe('getAllOccurrences – weeklyOne', () => {
    it('weekly on Monday returns only Mondays', () => {
      const meta: CalendarRepeatMeta = {kind: 'weeklyOne', weekday: 1, endMode: 'after', afterCount: 4};
      const result = service.getAllOccurrences(meta, BASE_DATE);
      expect(result).toHaveLength(4);
      result.forEach(ts => expect(weekdayOf(ts)).toBe(1));
    });

    it('consecutive weekly occurrences are 7 calendar days apart', () => {
      const meta: CalendarRepeatMeta = {kind: 'weeklyOne', weekday: 1, endMode: 'after', afterCount: 3};
      const result = service.getAllOccurrences(meta, BASE_DATE);
      // Use date arithmetic to avoid DST millisecond drift
      const d0 = new Date(result[0]), d1 = new Date(result[1]), d2 = new Date(result[2]);
      expect(d1.getDate() - d0.getDate()).toBe(7);
      expect(d2.getDate() - d1.getDate()).toBe(7);
    });

    it('weekly on Friday starting from Monday advances to that Friday', () => {
      const meta: CalendarRepeatMeta = {kind: 'weeklyOne', weekday: 5, endMode: 'after', afterCount: 2};
      const result = service.getAllOccurrences(meta, BASE_DATE);
      expect(weekdayOf(result[0])).toBe(5); // Friday
    });
  });

  describe('getAllOccurrences – weeklyMulti', () => {
    it('Mon+Wed+Fri pattern only produces those weekdays', () => {
      const meta: CalendarRepeatMeta = {
        kind: 'weeklyMulti', weekdays: [1, 3, 5], endMode: 'after', afterCount: 6
      };
      const result = service.getAllOccurrences(meta, BASE_DATE);
      expect(result).toHaveLength(6);
      result.forEach(ts => expect([1, 3, 5]).toContain(weekdayOf(ts)));
    });
  });

  describe('getAllOccurrences – monthlyDom', () => {
    it('returns the same day-of-month each month', () => {
      const meta: CalendarRepeatMeta = {kind: 'monthlyDom', dom: 16, endMode: 'after', afterCount: 4};
      const result = service.getAllOccurrences(meta, BASE_DATE);
      expect(result).toHaveLength(4);
      result.forEach(ts => expect(dayOf(ts)).toBe(16));
    });
  });

  describe('getAllOccurrences – yearlyOne', () => {
    it('returns the same month and day each year', () => {
      const meta: CalendarRepeatMeta = {kind: 'yearlyOne', dom: 16, month: 2, endMode: 'after', afterCount: 3};
      const result = service.getAllOccurrences(meta, BASE_DATE);
      expect(result).toHaveLength(3);
      result.forEach(ts => {
        const d = new Date(ts);
        expect(d.getMonth()).toBe(2);  // March
        expect(d.getDate()).toBe(16);
      });
      // Consecutive years
      expect(new Date(result[1]).getFullYear() - new Date(result[0]).getFullYear()).toBe(1);
    });
  });

  describe('getAllOccurrences – everyNWeekOne', () => {
    it('every 2 weeks on Monday returns Mondays 14 calendar days apart', () => {
      const meta: CalendarRepeatMeta = {kind: 'everyNWeekOne', n: 2, weekday: 1, endMode: 'after', afterCount: 3};
      const result = service.getAllOccurrences(meta, BASE_DATE);
      expect(result).toHaveLength(3);
      result.forEach(ts => expect(weekdayOf(ts)).toBe(1));
      // Math.round handles ±1h DST variance in millisecond comparison
      expect(Math.round((result[1] - result[0]) / 86400000)).toBe(14);
    });
  });

  // ─── buildRepeatSelectOptions ──────────────────────────────────────────────

  describe('buildRepeatSelectOptions', () => {
    const monday = new Date('2026-03-16'); // Monday, day=16, month=2

    it('returns exactly 7 options', () => {
      expect(service.buildRepeatSelectOptions(monday)).toHaveLength(7);
    });

    it('first option is "none"', () => {
      const opts = service.buildRepeatSelectOptions(monday);
      expect(opts[0].value).toBe('none');
    });

    it('last option is "custom"', () => {
      const opts = service.buildRepeatSelectOptions(monday);
      expect(opts[opts.length - 1].value).toBe('custom');
    });

    it('weeklyOne option carries the correct weekday from the date', () => {
      const opts = service.buildRepeatSelectOptions(monday);
      const weeklyOpt = opts.find(o => o.meta?.kind === 'weeklyOne');
      expect(weeklyOpt).toBeDefined();
      expect(weeklyOpt!.meta!.weekday).toBe(1); // Monday = 1
    });

    it('monthlyDom option carries the correct day-of-month', () => {
      const opts = service.buildRepeatSelectOptions(monday);
      const monthlyOpt = opts.find(o => o.meta?.kind === 'monthlyDom');
      expect(monthlyOpt).toBeDefined();
      expect(monthlyOpt!.meta!.dom).toBe(16);
    });

    it('yearlyOne option carries the correct month and dom', () => {
      const opts = service.buildRepeatSelectOptions(monday);
      const yearlyOpt = opts.find(o => o.meta?.kind === 'yearlyOne');
      expect(yearlyOpt).toBeDefined();
      expect(yearlyOpt!.meta!.dom).toBe(16);
      expect(yearlyOpt!.meta!.month).toBe(2); // March = index 2
    });

    it('all non-custom options except "none" have a meta object', () => {
      const opts = service.buildRepeatSelectOptions(monday);
      opts.filter(o => o.value !== 'none' && o.value !== 'custom')
        .forEach(o => expect(o.meta).toBeDefined());
    });
  });

  // ─── buildMetaFromCustomConfig ────────────────────────────────────────────

  describe('buildMetaFromCustomConfig', () => {
    it('step=1 day maps to kind "daily"', () => {
      const meta = service.buildMetaFromCustomConfig(1, 'day', [], 'never');
      expect(meta.kind).toBe('daily');
    });

    it('step=3 day maps to kind "everyNd" with n=3', () => {
      const meta = service.buildMetaFromCustomConfig(3, 'day', [], 'after', 5);
      expect(meta.kind).toBe('everyNd');
      expect(meta.n).toBe(3);
    });

    it('step=1 week, single weekday maps to kind "weeklyOne"', () => {
      const meta = service.buildMetaFromCustomConfig(1, 'week', [3], 'never');
      expect(meta.kind).toBe('weeklyOne');
      expect(meta.weekday).toBe(3);
    });

    it('step=1 week, multiple weekdays maps to kind "weeklyMulti"', () => {
      const meta = service.buildMetaFromCustomConfig(1, 'week', [1, 3, 5], 'never');
      expect(meta.kind).toBe('weeklyMulti');
      expect(meta.weekdays).toEqual([1, 3, 5]);
    });

    it('step=1 month maps to kind "monthlyDom"', () => {
      const meta = service.buildMetaFromCustomConfig(1, 'month', [], 'never');
      expect(meta.kind).toBe('monthlyDom');
    });

    it('endMode "after" is preserved with afterCount', () => {
      const meta = service.buildMetaFromCustomConfig(1, 'day', [], 'after', 10);
      expect(meta.endMode).toBe('after');
      expect(meta.afterCount).toBe(10);
    });

    it('endMode "until" is preserved with untilTs', () => {
      const ts = new Date('2027-01-01').getTime();
      const meta = service.buildMetaFromCustomConfig(1, 'day', [], 'until', undefined, ts);
      expect(meta.endMode).toBe('until');
      expect(meta.untilTs).toBe(ts);
    });
  });

  // ─── decomposeCustomMeta ─────────────────────────────────────────────────
  //
  // Each test builds a meta via buildMetaFromCustomConfig, decomposes it,
  // and asserts the decomposed values round-trip back to the inputs.

  describe('decomposeCustomMeta', () => {
    it('weeklyOne (step=1, weekday=Tue) round-trips', () => {
      const meta = service.buildMetaFromCustomConfig(1, 'week', [2], 'never');
      const d = service.decomposeCustomMeta(meta);
      expect(d.step).toBe(1);
      expect(d.unit).toBe('week');
      expect(d.weekdays).toEqual([2]);
      expect(d.endMode).toBe('never');
    });

    it('weeklyMulti (step=1, weekdays=[1,2]) round-trips', () => {
      const meta = service.buildMetaFromCustomConfig(1, 'week', [1, 2], 'never');
      const d = service.decomposeCustomMeta(meta);
      expect(d.step).toBe(1);
      expect(d.unit).toBe('week');
      expect(d.weekdays).toEqual([1, 2]);
    });

    it('weeklyAll (constructed directly — builder collapses to everyNWeekAll)', () => {
      // The in-app builder cannot emit `kind: 'weeklyAll'` because empty
      // weekdays always lands on `everyNWeekAll`. Construct the meta directly
      // to cover the explicit `weeklyAll` switch branch.
      const meta: CalendarRepeatMeta = {kind: 'weeklyAll', endMode: 'never'};
      const d = service.decomposeCustomMeta(meta);
      expect(d.step).toBe(1);
      expect(d.unit).toBe('week');
      expect(d.weekdays).toEqual([]);
    });

    it('everyNWeekAll-via-builder (step=1, weekdays=[]) → unit=week, weekdays=[]', () => {
      // Confirm the builder's collapse path: empty weekdays + step=1 still
      // collapses to everyNWeekAll, and decompose handles it.
      const meta = service.buildMetaFromCustomConfig(1, 'week', [], 'never');
      expect(meta.kind).toBe('everyNWeekAll');
      const d = service.decomposeCustomMeta(meta);
      expect(d.unit).toBe('week');
      expect(d.weekdays).toEqual([]);
    });

    it('everyNWeekOne (n=2, weekday=1) → step=2, unit=week, weekdays=[1]', () => {
      const meta = service.buildMetaFromCustomConfig(2, 'week', [1], 'never');
      const d = service.decomposeCustomMeta(meta);
      expect(d.step).toBe(2);
      expect(d.unit).toBe('week');
      expect(d.weekdays).toEqual([1]);
    });

    it('everyNWeekMulti (n=3, weekdays=[1,3,5]) → step=3, unit=week, weekdays=[1,3,5]', () => {
      const meta = service.buildMetaFromCustomConfig(3, 'week', [1, 3, 5], 'never');
      const d = service.decomposeCustomMeta(meta);
      expect(d.step).toBe(3);
      expect(d.unit).toBe('week');
      expect(d.weekdays).toEqual([1, 3, 5]);
    });

    it('everyNWeekAll (n=2) → weekdays=[]', () => {
      const meta = service.buildMetaFromCustomConfig(2, 'week', [], 'never');
      const d = service.decomposeCustomMeta(meta);
      expect(d.step).toBe(2);
      expect(d.unit).toBe('week');
      expect(d.weekdays).toEqual([]);
    });

    it('daily (step=1, day) round-trips', () => {
      const meta = service.buildMetaFromCustomConfig(1, 'day', [], 'never');
      const d = service.decomposeCustomMeta(meta);
      expect(d.step).toBe(1);
      expect(d.unit).toBe('day');
      expect(d.weekdays).toEqual([]);
    });

    it('everyNd (n=4) round-trips', () => {
      const meta = service.buildMetaFromCustomConfig(4, 'day', [], 'never');
      const d = service.decomposeCustomMeta(meta);
      expect(d.step).toBe(4);
      expect(d.unit).toBe('day');
      expect(d.weekdays).toEqual([]);
    });

    it('monthlyDom (step=1, month) round-trips', () => {
      const meta = service.buildMetaFromCustomConfig(1, 'month', [], 'never');
      const d = service.decomposeCustomMeta(meta);
      expect(d.step).toBe(1);
      expect(d.unit).toBe('month');
      expect(d.weekdays).toEqual([]);
    });

    it('everyNMonthDom (n=2) round-trips', () => {
      const meta = service.buildMetaFromCustomConfig(2, 'month', [], 'never');
      const d = service.decomposeCustomMeta(meta);
      expect(d.step).toBe(2);
      expect(d.unit).toBe('month');
      expect(d.weekdays).toEqual([]);
    });

    it('yearlyOne (step=1, year) round-trips', () => {
      const meta = service.buildMetaFromCustomConfig(1, 'year', [], 'never');
      const d = service.decomposeCustomMeta(meta);
      expect(d.step).toBe(1);
      expect(d.unit).toBe('year');
      expect(d.weekdays).toEqual([]);
    });

    it('everyNYear (n=5) round-trips', () => {
      const meta = service.buildMetaFromCustomConfig(5, 'year', [], 'never');
      const d = service.decomposeCustomMeta(meta);
      expect(d.step).toBe(5);
      expect(d.unit).toBe('year');
      expect(d.weekdays).toEqual([]);
    });

    it('endMode "after" with afterCount=7 round-trips', () => {
      const meta = service.buildMetaFromCustomConfig(1, 'week', [2], 'after', 7);
      const d = service.decomposeCustomMeta(meta);
      expect(d.endMode).toBe('after');
      expect(d.afterCount).toBe(7);
    });

    it('endMode "until" with untilTs=12345 round-trips', () => {
      const meta = service.buildMetaFromCustomConfig(1, 'week', [2], 'until', undefined, 12345);
      const d = service.decomposeCustomMeta(meta);
      expect(d.endMode).toBe('until');
      expect(d.untilTs).toBe(12345);
    });
  });

  // ─── reconstructMetaFromTask ─────────────────────────────────────────────
  //
  // Build-→-save-→-reconstruct round-trip per kind, plus per-built-in-rule
  // cases, edge cases that must return null, the legacy fallback, the
  // weekdays mapping, and the three end-mode paths. See spec
  // 2026-04-30-calendar-edit-mode-meta-reconstruction-design.md.

  describe('reconstructMetaFromTask', () => {
    // Mirrors task-create-edit-modal.component.ts:onSave's kindMap / save flow
    // — the minimum task shape the modal would emit for a given meta. Used as
    // the fixture for round-trip tests so we exercise the same fields the
    // backend would actually store.
    const KIND_MAP: Record<string, number> = {
      'daily': 1, 'everyNd': 1,
      'weeklyOne': 2, 'weeklyMulti': 2, 'everyNWeekOne': 2,
      'everyNWeekMulti': 2, 'everyNWeekAll': 2, 'weeklyAll': 2,
      'monthlyDom': 3, 'everyNMonthDom': 3,
      'yearlyOne': 4, 'everyNYear': 4,
    };

    // Constructs the "fake task" the way the modal save flow would emit
    // for a given meta, then forces repeatRule='custom' so we exercise the
    // 'custom' switch branch (which is the only one populated by the modal
    // for non-trivial step sizes via calendar-container.mapRepeatType).
    // Single-step weekly cases would naturally go through the built-in
    // branches; those are covered separately by the per-built-in-rule
    // tests further down.
    function fakeTaskFromMeta(
      meta: CalendarRepeatMeta,
      taskDate: string,
    ): CalendarTaskModel {
      const repeatType = KIND_MAP[meta.kind] ?? 0;
      const repeatEvery = meta.n ?? 1;
      const repeatEndMode = meta.endMode === 'after' ? 1
        : meta.endMode === 'until' ? 2 : 0;
      const repeatOccurrences = meta.endMode === 'after'
        ? meta.afterCount ?? null : null;
      const repeatUntilDate = meta.endMode === 'until' && meta.untilTs != null
        ? new Date(meta.untilTs).toISOString() : null;
      // The save flow only writes a CSV when meta.weekdays?.length is truthy.
      // Single-weekday metas (weeklyOne/everyNWeekOne) leave it null and rely
      // on dayOfWeek; multi-day metas write the CSV.
      const repeatWeekdaysCsv = meta.weekdays?.length
        ? meta.weekdays.join(',') : null;
      const dayOfWeek = meta.weekday ?? null;
      const dayOfMonth = meta.dom ?? null;

      return {
        id: 1, title: 't', startHour: 9, duration: 1, startText: '09:00',
        endText: '10:00', tags: [], assigneeIds: [], boardId: 1, color: '',
        descriptionHtml: '', repeatRule: 'custom', taskDate,
        completed: false, propertyId: 1,
        repeatType, repeatEvery, repeatEndMode, repeatOccurrences,
        repeatUntilDate, dayOfWeek, dayOfMonth, repeatWeekdaysCsv,
      } as CalendarTaskModel;
    }

    // taskDate the fakeTask uses; UTC-March is month index 2 so yearlyOne's
    // monthFromTaskDate() reconstructs correctly.
    const TASK_DATE = '2026-03-16';

    // ----- Round-trip per kind (12) -----------------------------------------

    it('round-trip: daily (step=1)', () => {
      const meta = service.buildMetaFromCustomConfig(1, 'day', [], 'never');
      const task = fakeTaskFromMeta(meta, TASK_DATE);
      const reconstructed = service.reconstructMetaFromTask(task);
      expect(reconstructed?.kind).toBe('daily');
      expect(reconstructed?.n).toBe(1);
    });

    it('round-trip: everyNd (n=4)', () => {
      const meta = service.buildMetaFromCustomConfig(4, 'day', [], 'never');
      const task = fakeTaskFromMeta(meta, TASK_DATE);
      const reconstructed = service.reconstructMetaFromTask(task);
      expect(reconstructed?.kind).toBe('everyNd');
      expect(reconstructed?.n).toBe(4);
    });

    // Single-weekday weekly metas (weeklyOne / everyNWeekOne) are NOT
    // round-trippable through the 'custom' branch because the save flow
    // doesn't write the weekday into RepeatWeekdaysCsv when there's only
    // one. Those rules persist via dayOfWeek + repeatType=2 and load via
    // mapRepeatType → 'weeklyOne' (built-in branch). The dedicated
    // per-built-in-rule test below covers weeklyOne from that path.

    it('round-trip: weeklyMulti (Mon+Wed+Fri)', () => {
      const meta = service.buildMetaFromCustomConfig(1, 'week', [1, 3, 5], 'never');
      const task = fakeTaskFromMeta(meta, TASK_DATE);
      const reconstructed = service.reconstructMetaFromTask(task);
      expect(reconstructed?.kind).toBe('weeklyMulti');
      expect(reconstructed?.weekdays).toEqual([1, 3, 5]);
    });

    // everyNWeekOne with single weekday is the documented legacy-fallback
    // case: type=2 + step!=1 + null CSV → reconstructed as everyNWeekAll
    // (because the save flow doesn't write a single-weekday CSV). Users
    // re-save once to get a clean rule. This is intentional per spec.

    it('round-trip: everyNWeekMulti (n=3, Mon+Wed+Fri)', () => {
      const meta = service.buildMetaFromCustomConfig(3, 'week', [1, 3, 5], 'never');
      const task = fakeTaskFromMeta(meta, TASK_DATE);
      const reconstructed = service.reconstructMetaFromTask(task);
      expect(reconstructed?.kind).toBe('everyNWeekMulti');
      expect(reconstructed?.n).toBe(3);
      expect(reconstructed?.weekdays).toEqual([1, 3, 5]);
    });

    it('round-trip: everyNWeekAll (n=2, no weekdays)', () => {
      const meta = service.buildMetaFromCustomConfig(2, 'week', [], 'never');
      const task = fakeTaskFromMeta(meta, TASK_DATE);
      const reconstructed = service.reconstructMetaFromTask(task);
      // Builder collapses empty weekdays to everyNWeekAll → reconstruction
      // for type=2 + empty CSV maps back to the same kind.
      expect(reconstructed?.kind).toBe('everyNWeekAll');
      expect(reconstructed?.n).toBe(2);
    });

    it('round-trip: weeklyAll (constructed directly — type=2, empty CSV, n=1)', () => {
      // Builder always lands on 'everyNWeekAll' for empty weekdays, so we
      // construct the meta directly to exercise the n=1 branch of
      // reconstruction's case 2 / days.length === 0 path.
      const meta: CalendarRepeatMeta = {kind: 'weeklyAll', endMode: 'never'};
      const task = fakeTaskFromMeta(meta, TASK_DATE);
      const reconstructed = service.reconstructMetaFromTask(task);
      expect(reconstructed?.kind).toBe('weeklyAll');
    });

    it('round-trip: monthlyDom (step=1)', () => {
      const meta = service.buildMetaFromCustomConfig(1, 'month', [], 'never');
      // buildMetaFromCustomConfig sets dom=1; carry that into the fake task.
      const task = fakeTaskFromMeta(meta, TASK_DATE);
      const reconstructed = service.reconstructMetaFromTask(task);
      expect(reconstructed?.kind).toBe('monthlyDom');
      expect(reconstructed?.dom).toBe(1);
    });

    it('round-trip: everyNMonthDom (n=2)', () => {
      const meta = service.buildMetaFromCustomConfig(2, 'month', [], 'never');
      const task = fakeTaskFromMeta(meta, TASK_DATE);
      const reconstructed = service.reconstructMetaFromTask(task);
      expect(reconstructed?.kind).toBe('everyNMonthDom');
      expect(reconstructed?.n).toBe(2);
      expect(reconstructed?.dom).toBe(1);
    });

    it('round-trip: yearlyOne (step=1)', () => {
      const meta = service.buildMetaFromCustomConfig(1, 'year', [], 'never');
      const task = fakeTaskFromMeta(meta, TASK_DATE);
      const reconstructed = service.reconstructMetaFromTask(task);
      expect(reconstructed?.kind).toBe('yearlyOne');
      // taskDate '2026-03-16' → getUTCMonth() === 2 (March)
      expect(reconstructed?.month).toBe(2);
      expect(reconstructed?.dom).toBe(1);
    });

    it('round-trip: everyNYear (n=5)', () => {
      const meta = service.buildMetaFromCustomConfig(5, 'year', [], 'never');
      const task = fakeTaskFromMeta(meta, TASK_DATE);
      const reconstructed = service.reconstructMetaFromTask(task);
      expect(reconstructed?.kind).toBe('everyNYear');
      expect(reconstructed?.n).toBe(5);
      expect(reconstructed?.month).toBe(2);
      expect(reconstructed?.dom).toBe(1);
    });

    // ----- Per built-in rule -------------------------------------------------

    function builtInTask(partial: Partial<CalendarTaskModel>): CalendarTaskModel {
      return {
        id: 1, title: 't', startHour: 9, duration: 1, startText: '09:00',
        endText: '10:00', tags: [], assigneeIds: [], boardId: 1, color: '',
        descriptionHtml: '', taskDate: TASK_DATE, completed: false,
        propertyId: 1, repeatRule: 'none', repeatEvery: 1, repeatEndMode: 0,
        ...partial,
      } as CalendarTaskModel;
    }

    it('built-in: repeatRule="daily" reconstructs as daily', () => {
      const r = service.reconstructMetaFromTask(builtInTask({repeatRule: 'daily'}));
      expect(r?.kind).toBe('daily');
    });

    it('built-in: repeatRule="weeklyOne" with dayOfWeek=3 reconstructs as weeklyOne', () => {
      const r = service.reconstructMetaFromTask(builtInTask({
        repeatRule: 'weeklyOne', dayOfWeek: 3,
      }));
      expect(r?.kind).toBe('weeklyOne');
      expect(r?.weekday).toBe(3);
    });

    it('built-in: repeatRule="weeklyAll" reconstructs as weeklyAll', () => {
      const r = service.reconstructMetaFromTask(builtInTask({repeatRule: 'weeklyAll'}));
      expect(r?.kind).toBe('weeklyAll');
    });

    it('built-in: repeatRule="monthlyDom" with dayOfMonth=15 reconstructs as monthlyDom', () => {
      const r = service.reconstructMetaFromTask(builtInTask({
        repeatRule: 'monthlyDom', dayOfMonth: 15,
      }));
      expect(r?.kind).toBe('monthlyDom');
      expect(r?.dom).toBe(15);
    });

    it('built-in: repeatRule="yearlyOne" with dayOfMonth=20 reconstructs as yearlyOne with month from taskDate', () => {
      const r = service.reconstructMetaFromTask(builtInTask({
        repeatRule: 'yearlyOne', dayOfMonth: 20, taskDate: '2026-03-16',
      }));
      expect(r?.kind).toBe('yearlyOne');
      expect(r?.dom).toBe(20);
      expect(r?.month).toBe(2); // March, 0-indexed via getUTCMonth
    });

    // ----- Edge cases — must return null ------------------------------------

    it('returns null when repeatRule === "none"', () => {
      expect(service.reconstructMetaFromTask(builtInTask({repeatRule: 'none'}))).toBeNull();
    });

    it('returns null when repeatRule is undefined', () => {
      const t = builtInTask({});
      // tsc would normally reject this — cast to any so the runtime null guard is exercised.
      (t as any).repeatRule = undefined;
      expect(service.reconstructMetaFromTask(t)).toBeNull();
    });

    it('returns null for an unknown repeatRule string', () => {
      const t = builtInTask({});
      (t as any).repeatRule = 'someUnknownString';
      expect(service.reconstructMetaFromTask(t)).toBeNull();
    });

    it('returns null when repeatRule === "weeklyOne" but dayOfWeek is null', () => {
      expect(service.reconstructMetaFromTask(builtInTask({
        repeatRule: 'weeklyOne', dayOfWeek: null,
      }))).toBeNull();
    });

    it('returns null when repeatRule === "custom" but repeatType is undefined', () => {
      const t = builtInTask({repeatRule: 'custom'});
      (t as any).repeatType = undefined;
      expect(service.reconstructMetaFromTask(t)).toBeNull();
    });

    it('returns null when repeatRule === "monthlyDom" but dayOfMonth is null', () => {
      expect(service.reconstructMetaFromTask(builtInTask({
        repeatRule: 'monthlyDom', dayOfMonth: null,
      }))).toBeNull();
    });

    // ----- Legacy fallback: type=2 + null CSV → weeklyAll/everyNWeekAll -----

    it('legacy fallback: repeatRule="custom", repeatType=2, repeatWeekdaysCsv=null, n=1 → weeklyAll', () => {
      const r = service.reconstructMetaFromTask(builtInTask({
        repeatRule: 'custom', repeatType: 2, repeatEvery: 1,
        repeatWeekdaysCsv: null,
      }));
      expect(r?.kind).toBe('weeklyAll');
      expect(r?.n).toBe(1);
    });

    it('legacy fallback: repeatRule="custom", repeatType=2, repeatWeekdaysCsv=null, n=3 → everyNWeekAll', () => {
      const r = service.reconstructMetaFromTask(builtInTask({
        repeatRule: 'custom', repeatType: 2, repeatEvery: 3,
        repeatWeekdaysCsv: null,
      }));
      expect(r?.kind).toBe('everyNWeekAll');
      expect(r?.n).toBe(3);
    });

    // ----- Weekdays mapping (Mon-Fri) → weeklyMulti --------------------------

    it('built-in "weekdays" with n=1 → weeklyMulti with weekdays=[1,2,3,4,5]', () => {
      // 'weekdays' is not in the CalendarRepeatRule union today — cast via
      // `as any` so this future-compat path is still exercised.
      const r = service.reconstructMetaFromTask(builtInTask({
        repeatRule: 'weekdays' as any, repeatEvery: 1,
      }));
      expect(r?.kind).toBe('weeklyMulti');
      expect(r?.weekdays).toEqual([1, 2, 3, 4, 5]);
    });

    it('formatCustomRepeatLabel renders explicit Mon-Fri list, not "all days"', () => {
      // Use a local TranslateService stub that performs basic {{key}}
      // interpolation so we can read meaningful text out of the formatter.
      // The base translateStub returns keys verbatim, which is fine for
      // shape-only assertions but would mask the days substitution here.
      const interpolatingStub = {
        instant: (key: string, params?: Record<string, any>) => {
          if (!params) return key;
          return Object.entries(params).reduce(
            (out, [k, v]) => out.replace(new RegExp(`{{\\s*${k}\\s*}}`, 'g'), String(v)),
            key,
          );
        },
      } as unknown as TranslateService;
      const localService = new CalendarRepeatService(interpolatingStub);

      const r = localService.reconstructMetaFromTask(builtInTask({
        repeatRule: 'weekdays' as any, repeatEvery: 1,
      }))!;
      // en-US locale; Intl.ListFormat handles weekday-name pluralisation.
      const label = localService.formatCustomRepeatLabel(r, 'en-US');
      // Intl.ListFormat ('en-US', conjunction, long) yields "Monday, Tuesday,
      // Wednesday, Thursday, and Friday" — assert each day is present so
      // serial-comma differences across runtimes don't break the test.
      expect(label).toContain('Monday');
      expect(label).toContain('Tuesday');
      expect(label).toContain('Wednesday');
      expect(label).toContain('Thursday');
      expect(label).toContain('Friday');
      // Must NOT collapse to the all-days summary.
      expect(label).not.toContain('Weekly on all days');
    });

    // ----- weeklyOne / weeklyAll promote to multi-day when CSV present -------

    it('weeklyOne with repeatWeekdaysCsv="1,3,5" promotes to weeklyMulti', () => {
      // Production case: a multi-day weekly rule saved at step=1.
      // calendar-container.mapRepeatType(2, 1) returns 'weeklyOne' regardless
      // of CSV, so the helper must consult the CSV column to recover the
      // multi-day intent rather than degrading to a single-day rule.
      const r = service.reconstructMetaFromTask(builtInTask({
        repeatRule: 'weeklyOne',
        repeatType: 2,
        repeatEvery: 1,
        repeatWeekdaysCsv: '1,3,5',
        dayOfWeek: 1,
      }));
      expect(r?.kind).toBe('weeklyMulti');
      expect(r?.weekdays).toEqual([1, 3, 5]);
      expect(r?.n).toBe(1);
    });

    it('weeklyOne with repeatWeekdaysCsv (single day) stays weeklyOne with that day', () => {
      // Edge case: CSV present with exactly one day. Should still resolve
      // through the CSV path so weekday=CSV[0], not task.dayOfWeek (which
      // could be stale from an earlier multi-day rule).
      const r = service.reconstructMetaFromTask(builtInTask({
        repeatRule: 'weeklyOne',
        repeatType: 2,
        repeatEvery: 1,
        repeatWeekdaysCsv: '4',
        dayOfWeek: 1,  // intentionally different — CSV must win
      }));
      expect(r?.kind).toBe('weeklyOne');
      expect(r?.weekday).toBe(4);
    });

    it('weeklyAll with CSV promotes to weeklyMulti (Mon-Fri example)', () => {
      const r = service.reconstructMetaFromTask(builtInTask({
        repeatRule: 'weeklyAll',
        repeatType: 2,
        repeatEvery: 1,
        repeatWeekdaysCsv: '1,2,3,4,5',
      }));
      expect(r?.kind).toBe('weeklyMulti');
      expect(r?.weekdays).toEqual([1, 2, 3, 4, 5]);
    });

    it('weeklyOne with no CSV (legacy single-day) reads task.dayOfWeek', () => {
      // Existing behaviour for pre-migration rows: no CSV, use the seeded
      // dayOfWeek field. Confirms the promotion is opt-in on CSV presence.
      const r = service.reconstructMetaFromTask(builtInTask({
        repeatRule: 'weeklyOne',
        repeatType: 2,
        repeatEvery: 1,
        repeatWeekdaysCsv: null,
        dayOfWeek: 3,
      }));
      expect(r?.kind).toBe('weeklyOne');
      expect(r?.weekday).toBe(3);
    });

    // ----- End-mode round-trip ---------------------------------------------

    it('end-mode "never" round-trips', () => {
      const t = builtInTask({
        repeatRule: 'daily', repeatEndMode: 0,
      });
      const r = service.reconstructMetaFromTask(t);
      expect(r?.endMode).toBe('never');
      expect(r?.afterCount).toBeUndefined();
      expect(r?.untilTs).toBeUndefined();
    });

    it('end-mode "after" with afterCount=7 round-trips', () => {
      const t = builtInTask({
        repeatRule: 'daily', repeatEndMode: 1, repeatOccurrences: 7,
      });
      const r = service.reconstructMetaFromTask(t);
      expect(r?.endMode).toBe('after');
      expect(r?.afterCount).toBe(7);
    });

    it('end-mode "until" with untilTs read from repeatUntilDate', () => {
      const ts = 12345;
      const t = builtInTask({
        repeatRule: 'daily', repeatEndMode: 2,
        repeatUntilDate: new Date(ts).toISOString(),
      });
      const r = service.reconstructMetaFromTask(t);
      expect(r?.endMode).toBe('until');
      expect(r?.untilTs).toBe(ts);
    });
  });
});
