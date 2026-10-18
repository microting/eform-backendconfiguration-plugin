import {describe, it, expect} from 'vitest';
import {ChemicalDeadlineKind} from '../../../models';
import {
  actionSeverity,
  addCalendarMonths,
  governingDeadline,
  isActionStatus,
  localToday,
  reachedThreshold,
  requiresAction,
} from './chemical-alert-rules.helper';

describe('chemical alert rules (TypeScript port of Plan D ChemicalAlertRules)', () => {
  it.each([
    ['2026-09-29', null],
    ['2026-09-30', 'sixMonths'],
    ['2027-02-27', 'sixMonths'],
    ['2027-02-28', 'oneMonth'],
    ['2027-03-31', 'oneMonth'],
    ['2027-04-01', 'passed'],
  ])('deadline 2027-03-31, today %s -> %s', (today, expected) => {
    expect(reachedThreshold('2027-03-31T00:00:00', today)).toBe(expected);
  });

  it.each([
    ['2027-08-28', null],
    ['2027-08-29', 'sixMonths'],
    ['2028-01-28', 'sixMonths'],
    ['2028-01-29', 'oneMonth'],
    ['2028-02-29', 'oneMonth'],
    ['2028-03-01', 'passed'],
  ])('leap-day deadline 2028-02-29, today %s -> %s', (today, expected) => {
    expect(reachedThreshold('2028-02-29T00:00:00', today)).toBe(expected);
  });

  it('uses the date part of a UTC timestamp without zone conversion', () => {
    expect(reachedThreshold('2027-03-31T22:00:00Z', '2027-03-31')).toBe('oneMonth');
    expect(reachedThreshold('2027-03-31T22:00:00Z', '2027-04-01')).toBe('passed');
  });

  it('has no threshold without a deadline', () => {
    expect(reachedThreshold(null, '2026-09-29')).toBeNull();
  });

  it('adds calendar months with month-end clamping, like DateOnly.AddMonths', () => {
    expect(addCalendarMonths('2027-03-31', -1)).toBe('2027-02-28');
    expect(addCalendarMonths('2028-02-29', -6)).toBe('2027-08-29');
    expect(addCalendarMonths('2026-08-31', 6)).toBe('2027-02-28');
    expect(addCalendarMonths('2026-12-15', 1)).toBe('2027-01-15');
  });

  it('RequiresAction vectors', () => {
    const e = (status: number | null, sales: string | null = null, use: string | null = null) =>
      ({status, salesDeadline: sales, useAndPossessionDeadline: use});

    expect(requiresAction(e(5), '2026-09-29')).toBe(false);
    expect(requiresAction(e(null), '2026-09-29')).toBe(false);
    expect(requiresAction(e(6), '2026-09-29')).toBe(true);
    expect(requiresAction(e(5, '2027-03-31'), '2026-09-30')).toBe(true);
    expect(requiresAction(e(5, null, '2026-09-30'), '2026-09-29')).toBe(true);
    expect(requiresAction(null, '2026-09-29')).toBe(false);
    expect(isActionStatus(7)).toBe(true);
    expect(isActionStatus(5)).toBe(false);
    expect(isActionStatus(8)).toBe(false);
    expect(isActionStatus(null)).toBe(false);
    expect(requiresAction(e(7, '2030-01-01', '2030-01-01'), '2026-10-01')).toBe(true);
    expect(requiresAction(e(5, '2030-01-01', '2030-01-01'), '2026-10-01')).toBe(false);
  });

  it('severity: red for passed or status 6/7, amber for 6 or 1 month, none otherwise', () => {
    expect(actionSeverity({status: 6, salesDeadline: null, useAndPossessionDeadline: null}, '2026-09-29')).toBe('overdue');
    expect(actionSeverity({status: 5, salesDeadline: '2026-09-28', useAndPossessionDeadline: null}, '2026-09-29')).toBe('overdue');
    expect(actionSeverity({status: 5, salesDeadline: null, useAndPossessionDeadline: '2027-01-01'}, '2026-09-29')).toBe('warning');
    expect(actionSeverity({status: 5, salesDeadline: null, useAndPossessionDeadline: '2030-01-01'}, '2026-09-29')).toBe('none');
    expect(actionSeverity(null, '2026-09-29')).toBe('none');
  });

  it('deadline cell: the most severe reached deadline, then the earliest, then none', () => {
    const today = '2026-09-29';

    expect(governingDeadline({status: 6, salesDeadline: '2026-03-01', useAndPossessionDeadline: '2027-03-01'}, today))
      .toEqual({date: '2026-03-01', kind: ChemicalDeadlineKind.Sales, threshold: 'passed'});
    expect(governingDeadline({status: 5, salesDeadline: '2027-02-01', useAndPossessionDeadline: '2026-10-15'}, today))
      .toEqual({date: '2026-10-15', kind: ChemicalDeadlineKind.UseAndPossession, threshold: 'oneMonth'});
    expect(governingDeadline({status: 5, salesDeadline: '2031-01-01', useAndPossessionDeadline: '2030-01-01'}, today))
      .toEqual({date: '2030-01-01', kind: ChemicalDeadlineKind.UseAndPossession, threshold: null});
    expect(governingDeadline({status: 5, salesDeadline: null, useAndPossessionDeadline: null}, today)).toBeNull();
  });

  it('deadline cell: equal severity and equal date -> use-and-possession wins', () => {
    expect(governingDeadline({status: 5, salesDeadline: '2026-10-15', useAndPossessionDeadline: '2026-10-15'}, '2026-09-29'))
      .toEqual({date: '2026-10-15', kind: ChemicalDeadlineKind.UseAndPossession, threshold: 'oneMonth'});
  });

  it('formats the local calendar date', () => {
    expect(localToday(new Date(2026, 8, 5, 23, 30))).toBe('2026-09-05');
  });
});
