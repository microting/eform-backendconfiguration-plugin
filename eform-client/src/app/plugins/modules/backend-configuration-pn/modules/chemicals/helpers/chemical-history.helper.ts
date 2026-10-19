import {
  ChemicalAlertThreshold,
  ChemicalDeadlineKind,
  ChemicalHistoryAction,
  ChemicalHistoryEventModel,
  ChemicalHistoryQueryModel,
  ChemicalRemovalReason,
  ChemicalStockUnit,
} from '../../../models';
import {ChemicalHistoryFiltersModel} from '../../../state';

/** The user id of the events nobody did by hand (BMD sync, alerts); the server lists real users only. */
export const SYSTEM_USER_ID = 0;
/** The default period: the last 30 days including today. */
export const HISTORY_DEFAULT_DAYS = 30;
/** ChemicalHistoryService.MaxPeriodDays: both ends included. */
export const HISTORY_MAX_DAYS = 366;
/** ChemicalHistoryService.MaxPageSize is 100. */
export const HISTORY_PAGE_SIZES = [25, 50, 100];

export const CHEMICAL_HISTORY_ACTIONS: ChemicalHistoryAction[] = Object.values(ChemicalHistoryAction)
  .filter((v): v is ChemicalHistoryAction => typeof v === 'number');

type Translate = (key: string) => string;

/** Calendar arithmetic on yyyy-MM-dd, free of the browser's time zone. */
export function addDays(ymd: string, days: number): string {
  const [y, m, d] = ymd.split('-').map(Number);
  return new Date(Date.UTC(y, m - 1, d + days)).toISOString().substring(0, 10);
}

function dayNumber(ymd: string): number {
  const [y, m, d] = ymd.split('-').map(Number);
  return Date.UTC(y, m - 1, d) / 86_400_000;
}

const COPENHAGEN_DAY = new Intl.DateTimeFormat('en-CA', {timeZone: 'Europe/Copenhagen', year: 'numeric', month: '2-digit', day: '2-digit'});

/** Today as the server counts the period: the Europe/Copenhagen calendar day, yyyy-MM-dd ('en-CA' formats it so). */
export function copenhagenToday(now: Date = new Date()): string {
  return COPENHAGEN_DAY.format(now);
}

export function defaultHistoryPeriod(today: string): {from: string; to: string} {
  return {from: addDays(today, -(HISTORY_DEFAULT_DAYS - 1)), to: today};
}

/**
 * POST …/history body. From/To go over the wire as the exact yyyy-MM-dd strings (ChemicalHistoryRequest
 * refuses a time or an offset); an unset bound is the default period's.
 */
export function buildHistoryQuery(
  filters: ChemicalHistoryFiltersModel, today: string, page: number, pageSize: number,
): ChemicalHistoryQueryModel {
  const period = defaultHistoryPeriod(today);
  return {
    from: filters.dateFrom ?? period.from,
    to: filters.dateTo ?? period.to,
    userId: filters.userId,
    action: filters.action,
    locationId: filters.locationId,
    page,
    pageSize,
  };
}

/** The server's period rules, checked before asking it: the translation key of the problem, or null. */
export function historyPeriodError(query: Pick<ChemicalHistoryQueryModel, 'from' | 'to'>): string | null {
  const days = dayNumber(query.to) - dayNumber(query.from) + 1;
  if (days < 1) {
    return 'Chemicals: Period reversed';
  }
  return days > HISTORY_MAX_DAYS ? 'Chemicals: Period too long' : null;
}

export function historyActionKey(action: ChemicalHistoryAction): string {
  return `Chemicals: Action ${ChemicalHistoryAction[action]}`;
}

/**
 * The event time as an ISO string the date pipe reads as UTC. Defence only: restoreIsoDates (the Kemi service)
 * already emits "…Z", so this adds a "Z" just to an offset-less time; one that already has "Z" or "+hh:mm" is
 * left alone (appending "Z" would make it invalid).
 */
export function asUtcIso(at: string): string {
  return /(Z|[+-]\d{2}:?\d{2})$/i.test(at) ? at : `${at}Z`;
}

/**
 * The texts ChemicalHistoryDetailsFormatter (C#) asks its localizer for, by its ChemicalLocalizationKeys name,
 * and the web key that carries the same words. Flags are looked up as "ChemicalFlag" + the flag name, as in C#.
 */
export const HISTORY_TEXT_KEYS: Record<string, string> = {
  ChemicalColumnRemovalReason: 'Chemicals: History reason',
  ChemicalReasonUsed: 'Chemicals: Reason used',
  ChemicalReasonDisposed: 'Chemicals: Reason disposed',
  ChemicalReasonMoved: 'Chemicals: History reason moved',
  ChemicalBalance: 'Chemicals: History balance',
  ChemicalStockOn: 'Chemicals: Stock turned on',
  ChemicalStockOff: 'Chemicals: Stock turned off',
  ChemicalDigestRecipients: 'Chemicals: History digest recipients',
  ChemicalDeadlineSales: 'Chemicals: Sales deadline',
  ChemicalDeadlineUseAndPossession: 'Chemicals: Use deadline',
  ChemicalThresholdSixMonths: 'Chemicals: History threshold six months',
  ChemicalThresholdOneMonth: 'Chemicals: History threshold one month',
  ChemicalThresholdPassed: 'Chemicals: Threshold passed',
  ChemicalThresholdStatusChanged: 'Chemicals: History threshold status changed',
  ChemicalFlagView: 'Chemicals: Flag view',
  ChemicalFlagRegister: 'Chemicals: Flag register',
  ChemicalFlagRemove: 'Chemicals: Flag remove',
  ChemicalFlagStock: 'Chemicals: Flag stock',
  ChemicalFlagManageLocations: 'Chemicals: Flag manage locations',
  ChemicalFlagAdmin: 'Chemicals: Flag admin',
};

/** C# decimal.ToString("0.###", InvariantCulture): at most three decimals, no trailing zeros, no grouping. */
export function formatHistoryNumber(value: number): string {
  const rounded = Math.round(Math.abs(value) * 1000) / 1000;
  return value < 0 && rounded !== 0 ? `-${rounded}` : String(rounded);
}

function unitText(unit: ChemicalStockUnit | null): string {
  return unit === ChemicalStockUnit.L ? 'L' : unit === ChemicalStockUnit.Kg ? 'kg' : '';
}

function reasonKey(reason: ChemicalRemovalReason | null): string {
  return reason === ChemicalRemovalReason.Used ? 'ChemicalReasonUsed'
    : reason === ChemicalRemovalReason.Disposed ? 'ChemicalReasonDisposed' : 'ChemicalReasonMoved';
}

function thresholdKey(threshold: ChemicalAlertThreshold | null): string {
  switch (threshold) {
    case ChemicalAlertThreshold.SixMonths:
      return 'ChemicalThresholdSixMonths';
    case ChemicalAlertThreshold.OneMonth:
      return 'ChemicalThresholdOneMonth';
    case ChemicalAlertThreshold.Passed:
      return 'ChemicalThresholdPassed';
    default:
      return 'ChemicalThresholdStatusChanged';
  }
}

function deadlineKey(kind: ChemicalDeadlineKind | null): string {
  return kind === ChemicalDeadlineKind.Sales ? 'ChemicalDeadlineSales' : 'ChemicalDeadlineUseAndPossession';
}

/** C# string.Join(" · ", parts.Where(p => !string.IsNullOrWhiteSpace(p))). */
function join(...parts: string[]): string {
  return parts.filter((p) => !!p && p.trim() !== '').join(' · ');
}

/**
 * W6 "Detaljer": a line-for-line port of ChemicalHistoryDetailsFormatter.Format (the Excel export), so the
 * page and the file say the same. `t` translates a web key; the vectors in the spec are the C# test's.
 */
export function formatHistoryDetails(e: ChemicalHistoryEventModel, t: Translate): string {
  const text = (csharpKey: string) => t(HISTORY_TEXT_KEYS[csharpKey] ?? csharpKey);
  const number = formatHistoryNumber;
  const unit = unitText(e.unit);
  const amount = (value: number | null) => (value === null || value === undefined ? '' : `${number(value)} ${unit}`);
  switch (e.action) {
    case ChemicalHistoryAction.Registered:
      return e.note;
    case ChemicalHistoryAction.Moved:
      return join(`${e.locationName} → ${e.targetLocationName}`, amount(e.amount), e.note);
    case ChemicalHistoryAction.Removed:
      return join(`${text('ChemicalColumnRemovalReason')}: ${text(reasonKey(e.removalReason))}`, amount(e.amount), e.note);
    case ChemicalHistoryAction.NoteChanged:
    case ChemicalHistoryAction.LocationRenamed:
      return `${e.oldValue} → ${e.newValue}`;
    case ChemicalHistoryAction.StockReceived:
    case ChemicalHistoryAction.StockConsumed:
      return join(amount(e.amount), `${text('ChemicalBalance')} ${amount(e.balanceAfter)}`, e.note);
    case ChemicalHistoryAction.StockAdjusted:
      return join(`${text('ChemicalBalance')} ${number((e.balanceAfter ?? 0) - (e.amount ?? 0))} → ${number(e.balanceAfter ?? 0)} ${unit}`, e.note);
    case ChemicalHistoryAction.LocationCreated:
    case ChemicalHistoryAction.LocationArchived:
    case ChemicalHistoryAction.LocationPhotoChanged:
      return e.locationName;
    case ChemicalHistoryAction.PermissionChanged:
      return `${e.workerName}: ` + [
        ...e.permissionsAdded.map((f) => `+${text(`ChemicalFlag${f}`)}`),
        ...e.permissionsRemoved.map((f) => `−${text(`ChemicalFlag${f}`)}`),
      ].join(', ');
    case ChemicalHistoryAction.SettingsChanged:
      return e.field === 'StockEnabled'
        ? text(e.newValue === 'true' ? 'ChemicalStockOn' : 'ChemicalStockOff')
        : `${text('ChemicalDigestRecipients')}: ${e.newValue}`;
    case ChemicalHistoryAction.StatusChanged:
      return e.statusText;
    case ChemicalHistoryAction.AlertSent:
      return join(`${text(thresholdKey(e.threshold))} (${text(deadlineKey(e.deadlineKind))})`, `push ${e.pushCount}`, `e-mail ${e.emailCount}`);
    default:
      return '';
  }
}
