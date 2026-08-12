import {
  assignedToDisplay,
  deadlineVisual,
  resolveAreaName,
  resolvePropertyName,
  resolveTagNames,
  resolveWorkerName,
  resolveWorkerNames,
} from '../../adhoc-display.util';
import {AdhocAreaModel, AdhocPropertyModel, AdhocTagModel, AdhocWorkerModel} from '../../../../models';

// Per the M5/F5 plan: pure-function level tests for the deadline banding
// and assignedTo/"Alle" logic used by adhoc-table.component (and reused by
// the drawer/history components). Authored, not run - jest runs from the
// host frontend only (repo convention; see docs/superpowers noted in the
// F1-F4 report for this milestone).
describe('adhoc-display.util', () => {
  const properties: AdhocPropertyModel[] = [{id: 1, name: 'Gård Nord'}];
  const areas: AdhocAreaModel[] = [{id: 10, propertyId: 1, name: 'Stald 1'}];
  const workers: AdhocWorkerModel[] = [
    {workerId: 100, displayName: 'Mette Hansen', propertyIds: [1]},
    {workerId: 101, displayName: 'Erik Eriksen', propertyIds: [1]},
  ];
  const tags: AdhocTagModel[] = [
    {id: 1000, name: 'Vedligehold', isUserTag: false},
    {id: 1001, name: 'Sikkerhed', isUserTag: false},
  ];

  describe('resolvePropertyName/resolveAreaName/resolveWorkerName/resolveTagNames', () => {
    it('resolves a known id to its name', () => {
      expect(resolvePropertyName(properties, 1)).toBe('Gård Nord');
      expect(resolveAreaName(areas, 10)).toBe('Stald 1');
      expect(resolveWorkerName(workers, 100)).toBe('Mette Hansen');
    });

    it('returns an empty string for a null/undefined/unknown id', () => {
      expect(resolvePropertyName(properties, null)).toBe('');
      expect(resolvePropertyName(properties, undefined)).toBe('');
      expect(resolvePropertyName(properties, 999)).toBe('');
      expect(resolveAreaName(areas, null)).toBe('');
      expect(resolveWorkerName(workers, null)).toBe('');
    });

    it('resolveWorkerNames maps multiple ids and drops unresolved ones', () => {
      expect(resolveWorkerNames(workers, [100, 101])).toEqual(['Mette Hansen', 'Erik Eriksen']);
      expect(resolveWorkerNames(workers, [100, 999])).toEqual(['Mette Hansen']);
      expect(resolveWorkerNames(workers, [])).toEqual([]);
      expect(resolveWorkerNames(workers, null)).toEqual([]);
    });

    it('resolveTagNames maps multiple ids and drops unresolved ones', () => {
      expect(resolveTagNames(tags, [1000, 1001])).toEqual(['Vedligehold', 'Sikkerhed']);
      expect(resolveTagNames(tags, [1000, 9999])).toEqual(['Vedligehold']);
    });
  });

  describe('assignedToDisplay', () => {
    // Issue #1085: executionRule and assignedWorkerIds are independent, so a
    // task can be assigned to named workers AND "everyone" at the same time -
    // both the name chips and the "Alle" chip must render.
    it('executionRule 1 (everyone) with assignedWorkerIds returns everyone:true AND the resolved names', () => {
      const result = assignedToDisplay(1, [100, 101], workers);
      expect(result.everyone).toBe(true);
      expect(result.names).toEqual(['Mette Hansen', 'Erik Eriksen']);
    });

    it('executionRule 1 (everyone) with no assignees returns everyone:true with no names', () => {
      const result = assignedToDisplay(1, [], workers);
      expect(result.everyone).toBe(true);
      expect(result.names).toEqual([]);

      const nullResult = assignedToDisplay(1, null, workers);
      expect(nullResult.everyone).toBe(true);
      expect(nullResult.names).toEqual([]);
    });

    it('executionRule 0 (assignedOnly) resolves the assigned worker names', () => {
      const result = assignedToDisplay(0, [100], workers);
      expect(result.everyone).toBe(false);
      expect(result.names).toEqual(['Mette Hansen']);
    });

    it('executionRule 0 with no assignees returns an empty names list', () => {
      const result = assignedToDisplay(0, [], workers);
      expect(result.everyone).toBe(false);
      expect(result.names).toEqual([]);
    });
  });

  describe('deadlineVisual', () => {
    const today = new Date(2026, 3, 15); // 2026-04-15 (local, matches mockup fixtures' month)

    it('no deadline -> band "none", pct 0, days null', () => {
      const v = deadlineVisual(null, today);
      expect(v.band).toBe('none');
      expect(v.pct).toBe(0);
      expect(v.days).toBeNull();
    });

    it('a past date -> band "overdue", pct 100, negative days', () => {
      const v = deadlineVisual('2026-04-10', today);
      expect(v.band).toBe('overdue');
      expect(v.pct).toBe(100);
      expect(v.days).toBe(-5);
    });

    it('today -> band "today", pct 94, days 0', () => {
      const v = deadlineVisual('2026-04-15', today);
      expect(v.band).toBe('today');
      expect(v.pct).toBe(94);
      expect(v.days).toBe(0);
    });

    it('1-5 days away -> band "soon"', () => {
      const v1 = deadlineVisual('2026-04-16', today);
      expect(v1.band).toBe('soon');
      expect(v1.days).toBe(1);
      expect(v1.pct).toBe(88);

      const v5 = deadlineVisual('2026-04-20', today);
      expect(v5.band).toBe('soon');
      expect(v5.days).toBe(5);
      expect(v5.pct).toBe(68);
    });

    it('6+ days away -> band "far"', () => {
      const v = deadlineVisual('2026-04-25', today);
      expect(v.band).toBe('far');
      expect(v.days).toBe(10);
      expect(v.pct).toBeLessThan(58);
      expect(v.pct).toBeGreaterThanOrEqual(12);
    });

    it('far in the future clamps pct to the 12 floor', () => {
      const v = deadlineVisual('2027-04-25', today);
      expect(v.band).toBe('far');
      expect(v.pct).toBe(12);
    });
  });
});
