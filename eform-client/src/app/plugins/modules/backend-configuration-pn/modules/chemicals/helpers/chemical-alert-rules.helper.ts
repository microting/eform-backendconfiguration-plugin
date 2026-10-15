import {
  CHEMICAL_STATUS_DEREGISTERED,
  CHEMICAL_STATUS_EXPIRED,
  ChemicalDeadlineKind,
  ChemicalRegisterEntryModel,
} from '../../../models';

/**
 * TypeScript port of Plan D's ChemicalAlertRules (base package 10.0.57, Plan D
 * Contract C1), the one "Kræver handling" rule (spec §9). Pinned by the C1
 * vectors in the spec file. Dates are yyyy-MM-dd strings; a deadline is the date
 * part of the stored value with no zone conversion; "today" is the browser's
 * local date (the server uses Europe/Copenhagen, the app the device date).
 */
export type ChemicalThreshold = 'sixMonths' | 'oneMonth' | 'passed';
export type ChemicalSeverity = 'none' | 'warning' | 'overdue';
export type ChemicalRuleInput = Pick<ChemicalRegisterEntryModel, 'status' | 'salesDeadline' | 'useAndPossessionDeadline'>;

export interface ChemicalDeadlineDisplay {
  date: string;
  kind: ChemicalDeadlineKind;
  threshold: ChemicalThreshold | null;
}

const SEVERITY_ORDER: Record<ChemicalThreshold, number> = {sixMonths: 1, oneMonth: 2, passed: 3};

function pad(n: number, width = 2): string {
  return String(n).padStart(width, '0');
}

export function localToday(now: Date = new Date()): string {
  return `${pad(now.getFullYear(), 4)}-${pad(now.getMonth() + 1)}-${pad(now.getDate())}`;
}

/** DateOnly.AddMonths: calendar months, day clamped to the target month's length. */
export function addCalendarMonths(ymd: string, months: number): string {
  const [y, m, d] = ymd.split('-').map(Number);
  const total = y * 12 + (m - 1) + months;
  const year = Math.floor(total / 12);
  const month = total - year * 12 + 1;
  const daysInMonth = new Date(Date.UTC(year, month, 0)).getUTCDate();
  return `${pad(year, 4)}-${pad(month)}-${pad(Math.min(d, daysInMonth))}`;
}

export function reachedThreshold(deadline: string | null, today: string): ChemicalThreshold | null {
  if (!deadline) {
    return null;
  }
  const date = deadline.substring(0, 10);
  if (today > date) {
    return 'passed';
  }
  if (today >= addCalendarMonths(date, -1)) {
    return 'oneMonth';
  }
  return today >= addCalendarMonths(date, -6) ? 'sixMonths' : null;
}

export function isActionStatus(status: number | null): boolean {
  return status === CHEMICAL_STATUS_DEREGISTERED || status === CHEMICAL_STATUS_EXPIRED;
}

export function requiresAction(entry: ChemicalRuleInput | null, today: string): boolean {
  return actionSeverity(entry, today) !== 'none';
}

/** Row colour (Plan D C1): red when a deadline has passed or the status is 6/7, amber for 6/1 months. */
export function actionSeverity(entry: ChemicalRuleInput | null, today: string): ChemicalSeverity {
  if (!entry) {
    return 'none';
  }
  const thresholds = [reachedThreshold(entry.salesDeadline, today), reachedThreshold(entry.useAndPossessionDeadline, today)];
  if (isActionStatus(entry.status) || thresholds.includes('passed')) {
    return 'overdue';
  }
  return thresholds.some((t) => t !== null) ? 'warning' : 'none';
}

/** W1 "Frist" cell: most severe reached deadline (ties: earlier), else the earliest deadline, else null. */
export function governingDeadline(entry: ChemicalRuleInput | null, today: string): ChemicalDeadlineDisplay | null {
  if (!entry) {
    return null;
  }
  const candidates: ChemicalDeadlineDisplay[] = [
    {value: entry.useAndPossessionDeadline, kind: ChemicalDeadlineKind.UseAndPossession},
    {value: entry.salesDeadline, kind: ChemicalDeadlineKind.Sales},
  ]
    .filter((c) => !!c.value)
    .map((c) => ({date: c.value.substring(0, 10), kind: c.kind, threshold: reachedThreshold(c.value, today)}));
  if (!candidates.length) {
    return null;
  }
  return candidates.sort((a, b) => {
    const bySeverity = (b.threshold ? SEVERITY_ORDER[b.threshold] : 0) - (a.threshold ? SEVERITY_ORDER[a.threshold] : 0);
    return bySeverity !== 0 ? bySeverity : a.date.localeCompare(b.date);
  })[0];
}
