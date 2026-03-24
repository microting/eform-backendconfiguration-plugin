import {CalendarRepeatService} from './calendar-repeat.service';
import {CalendarRepeatMeta} from '../../../models/calendar';

// Monday 2026-03-16 00:00:00 UTC
const BASE_DATE = new Date('2026-03-16T00:00:00').getTime();

function dayOf(ts: number): number {
  return new Date(ts).getDate();
}

function weekdayOf(ts: number): number {
  return new Date(ts).getDay();
}

describe('CalendarRepeatService', () => {
  let service: CalendarRepeatService;

  beforeEach(() => {
    service = new CalendarRepeatService();
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
});
