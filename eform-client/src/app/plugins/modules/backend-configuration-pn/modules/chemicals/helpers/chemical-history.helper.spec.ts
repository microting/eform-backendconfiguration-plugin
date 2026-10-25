import {describe, it, expect} from 'vitest';
import {
  ChemicalAlertThreshold,
  ChemicalDeadlineKind,
  ChemicalHistoryAction,
  ChemicalHistoryEventModel,
  ChemicalRemovalReason,
  ChemicalStockUnit,
} from '../../../models';
import {
  CHEMICAL_HISTORY_ACTIONS,
  HISTORY_PAGE_SIZES,
  HISTORY_TEXT_KEYS,
  SYSTEM_USER_ID,
  addDays,
  asUtcIso,
  buildHistoryQuery,
  copenhagenToday,
  defaultHistoryPeriod,
  formatHistoryDetails,
  formatHistoryNumber,
  historyActionKey,
  historyPeriodError,
  toYmd,
} from './chemical-history.helper';

/**
 * E-13: the web "Detaljer" column and the Excel export (ChemicalHistoryDetailsFormatter, C#) must say the same.
 * `t` answers "<C# key>" for the web key that carries that C# text, the same passthrough as
 * ChemicalHistoryDetailsFormatterTests.Key, so every expected string below is that C# test's vector, byte for byte.
 */
const CSHARP_KEY_OF: Record<string, string> = Object.fromEntries(Object.entries(HISTORY_TEXT_KEYS).map(([cs, web]) => [web, cs]));
const t = (key: string) => `<${CSHARP_KEY_OF[key] ?? key}>`;

function event(partial: Partial<ChemicalHistoryEventModel>): ChemicalHistoryEventModel {
  // ChemicalHistoryEventModel's C# defaults: empty strings, empty lists, nulls, zero counts.
  return {
    at: '2026-09-28T12:05:00', userId: 5, userName: 'Mette Larsen', action: ChemicalHistoryAction.Registered,
    placementId: null, chemicalId: null, productName: '', registrationNo: '', locationId: null, locationName: '',
    targetLocationId: null, targetLocationName: '', amount: null, unit: null, balanceAfter: null, removalReason: null,
    note: '', field: '', oldValue: '', newValue: '', status: null, statusText: '', threshold: null, deadlineKind: null,
    pushCount: 0, emailCount: 0, workerName: '', permissionsAdded: [], permissionsRemoved: [], ...partial,
  };
}

const A = ChemicalHistoryAction;
const {L, Kg} = ChemicalStockUnit;

/** [C# test name, event, the C# test's expected string]. Task 10 report vectors 1-22. */
const VECTORS: Array<[string, Partial<ChemicalHistoryEventModel>, string]> = [
  ['Moved', {action: A.Moved, locationName: 'Lade', targetLocationName: 'Kemirum', amount: 5, unit: L, note: 'Reol 1'},
    'Lade → Kemirum · 5 L · Reol 1'],
  ['Consumed', {action: A.StockConsumed, amount: -0.5, unit: Kg, balanceAfter: 3.5},
    '-0.5 kg · <ChemicalBalance> 3.5 kg'],
  ['Received', {action: A.StockReceived, amount: 10, unit: L, balanceAfter: 12.25, note: 'Levering'},
    '10 L · <ChemicalBalance> 12.25 L · Levering'],
  ['Adjusted_ShowsTheCountBeforeAndAfter', {action: A.StockAdjusted, amount: -0.5, unit: L, balanceAfter: 8},
    '<ChemicalBalance> 8.5 → 8 L'],
  ['Removed_WithoutAmount', {action: A.Removed, removalReason: ChemicalRemovalReason.Used, note: 'tom'},
    '<ChemicalColumnRemovalReason>: <ChemicalReasonUsed> · tom'],
  ['Removed_WithTheFoldedWriteOffAmount', {action: A.Removed, removalReason: ChemicalRemovalReason.Disposed, amount: -4, unit: L, note: 'tom'},
    '<ChemicalColumnRemovalReason>: <ChemicalReasonDisposed> · -4 L · tom'],
  ['Removed_WithAmountAndNoNote', {action: A.Removed, removalReason: ChemicalRemovalReason.Used, amount: -1.5, unit: Kg},
    '<ChemicalColumnRemovalReason>: <ChemicalReasonUsed> · -1.5 kg'],
  ['PermissionChanged', {action: A.PermissionChanged, workerName: 'Ole Nielsen', permissionsAdded: ['Register'], permissionsRemoved: ['Stock']},
    'Ole Nielsen: +<ChemicalFlagRegister>, −<ChemicalFlagStock>'],
  ['StockSetting_On', {action: A.SettingsChanged, field: 'StockEnabled', oldValue: 'false', newValue: 'true'},
    '<ChemicalStockOn>'],
  ['StockSetting_Off', {action: A.SettingsChanged, field: 'StockEnabled', oldValue: 'true', newValue: 'false'},
    '<ChemicalStockOff>'],
  ['DigestRecipientsSetting', {action: A.SettingsChanged, field: 'DigestRecipients', oldValue: '', newValue: 'a@example.com'},
    '<ChemicalDigestRecipients>: a@example.com'],
  ['AlertSent', {action: A.AlertSent, threshold: ChemicalAlertThreshold.Passed, deadlineKind: ChemicalDeadlineKind.UseAndPossession, pushCount: 2, emailCount: 3},
    '<ChemicalThresholdPassed> (<ChemicalDeadlineUseAndPossession>) · push 2 · e-mail 3'],
  ['RenamedAndStatus (renamed)', {action: A.LocationRenamed, oldValue: 'Kemirum', newValue: 'Kemirum 2'},
    'Kemirum → Kemirum 2'],
  ['RenamedAndStatus (status)', {action: A.StatusChanged, statusText: 'Produkt afmeldt'},
    'Produkt afmeldt'],
  ['ThreeDecimalQuantities_AreNotRounded (consumed)', {action: A.StockConsumed, amount: -0.125, unit: L, balanceAfter: 0.875},
    '-0.125 L · <ChemicalBalance> 0.875 L'],
  ['ThreeDecimalQuantities_AreNotRounded (received)', {action: A.StockReceived, amount: 0.004, unit: Kg, balanceAfter: 0.004},
    '0.004 kg · <ChemicalBalance> 0.004 kg'],
  ['ThreeDecimalQuantities_AreNotRounded (adjusted)', {action: A.StockAdjusted, amount: 0.125, unit: L, balanceAfter: 1.5},
    '<ChemicalBalance> 1.375 → 1.5 L'],
  ['ThreeDecimalQuantities_AreNotRounded (removed)', {action: A.Removed, removalReason: ChemicalRemovalReason.Used, amount: -0.004, unit: Kg},
    '<ChemicalColumnRemovalReason>: <ChemicalReasonUsed> · -0.004 kg'],
  ['Registered_ShowsTheNote', {action: A.Registered, note: 'Hylde 2'},
    'Hylde 2'],
  ['NoteChanged_ShowsOldAndNew', {action: A.NoteChanged, oldValue: 'Hylde 1', newValue: 'Hylde 2'},
    'Hylde 1 → Hylde 2'],
  ['LocationCreatedArchivedAndPhoto (created)', {action: A.LocationCreated, locationName: 'Lade'}, 'Lade'],
  ['LocationCreatedArchivedAndPhoto (archived)', {action: A.LocationArchived, locationName: 'Lade'}, 'Lade'],
  ['LocationCreatedArchivedAndPhoto (photo)', {action: A.LocationPhotoChanged, locationName: 'Lade'}, 'Lade'],
  ['Removed_WithoutAReason_IsTheMovedReason', {action: A.Removed, removalReason: null},
    '<ChemicalColumnRemovalReason>: <ChemicalReasonMoved>'],
  ['AlertSent_ForTheSalesDeadlineAndAStatusChange (six months)',
    {action: A.AlertSent, threshold: ChemicalAlertThreshold.SixMonths, deadlineKind: ChemicalDeadlineKind.Sales, pushCount: 0, emailCount: 1},
    '<ChemicalThresholdSixMonths> (<ChemicalDeadlineSales>) · push 0 · e-mail 1'],
  ['AlertSent_ForTheSalesDeadlineAndAStatusChange (status changed)',
    {action: A.AlertSent, threshold: null, deadlineKind: ChemicalDeadlineKind.Sales},
    '<ChemicalThresholdStatusChanged> (<ChemicalDeadlineSales>) · push 0 · e-mail 0'],
];

describe('chemical history helper', () => {
  it('writes a picked day as yyyy-MM-dd, from its local calendar date', () => {
    expect(toYmd(new Date(2026, 0, 5))).toBe('2026-01-05');
    expect(toYmd(new Date(2026, 11, 31, 23, 59))).toBe('2026-12-31');
  });

  it('adds days across month and year ends', () => {
    expect(addDays('2026-03-01', -1)).toBe('2026-02-28');
    expect(addDays('2026-12-31', 1)).toBe('2027-01-01');
    expect(addDays('2024-02-28', 1)).toBe('2024-02-29');
  });

  it('takes today as the Copenhagen calendar day, the one the server counts the period in (m-2)', () => {
    // 22:30 UTC on 28 September is 00:30 on the 29th in Copenhagen (CEST, +2).
    expect(copenhagenToday(new Date('2026-09-28T22:30:00Z'))).toBe('2026-09-29');
    expect(copenhagenToday(new Date('2026-09-28T21:30:00Z'))).toBe('2026-09-28');
    // Winter time (+1) and a year end.
    expect(copenhagenToday(new Date('2026-12-31T23:30:00Z'))).toBe('2027-01-01');
    expect(copenhagenToday(new Date('2026-12-31T22:30:00Z'))).toBe('2026-12-31');
  });

  it('defaults to the last 30 days including today', () => {
    expect(defaultHistoryPeriod('2026-09-29')).toEqual({from: '2026-08-31', to: '2026-09-29'});
  });

  it('sends date-only yyyy-MM-dd bounds and the filters', () => {
    const query = buildHistoryQuery({dateFrom: null, dateTo: null, userId: 0, action: 3, locationId: 10}, '2026-09-29', 2, 25);

    expect(query).toEqual({from: '2026-08-31', to: '2026-09-29', userId: 0, action: 3, locationId: 10, page: 2, pageSize: 25});
    expect(buildHistoryQuery({dateFrom: '2026-09-01', dateTo: '2026-09-15', userId: null, action: null, locationId: null}, '2026-09-29', 0, 25))
      .toEqual(expect.objectContaining({from: '2026-09-01', to: '2026-09-15'}));
    // ChemicalHistoryRequest.ToQuery parses exactly yyyy-MM-dd: no time, no offset, no "Z".
    expect(query.from).toMatch(/^\d{4}-\d{2}-\d{2}$/);
    expect(query.to).toMatch(/^\d{4}-\d{2}-\d{2}$/);
  });

  it('checks the period the way ChemicalHistoryService does (inclusive, at most 366 days)', () => {
    expect(historyPeriodError({from: '2026-09-29', to: '2026-09-29'})).toBeNull();
    expect(historyPeriodError({from: '2026-09-20', to: '2026-09-10'})).toBe('Chemicals: Period reversed');
    expect(historyPeriodError({from: '2025-09-29', to: '2026-09-29'})).toBeNull();
    expect(historyPeriodError({from: '2025-09-28', to: '2026-09-29'})).toBe('Chemicals: Period too long');
    // 2024 is a leap year: 2024-01-01..2024-12-31 is 366 days.
    expect(historyPeriodError({from: '2024-01-01', to: '2024-12-31'})).toBeNull();
  });

  it('pages within the server maximum', () => {
    expect(Math.max(...HISTORY_PAGE_SIZES)).toBeLessThanOrEqual(100);
    expect(HISTORY_PAGE_SIZES[0]).toBe(25);
  });

  it('has a translation key per action, named after the C# enum member', () => {
    expect(CHEMICAL_HISTORY_ACTIONS).toHaveLength(15);
    expect(CHEMICAL_HISTORY_ACTIONS).toEqual([1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15]);
    expect(historyActionKey(ChemicalHistoryAction.StockAdjusted)).toBe('Chemicals: Action StockAdjusted');
    expect(SYSTEM_USER_ID).toBe(0);
  });

  it('pins the enum integers to ChemicalHistoryActionEnum', () => {
    expect([A.Registered, A.Moved, A.Removed, A.NoteChanged, A.StockReceived, A.StockConsumed, A.StockAdjusted,
      A.LocationCreated, A.LocationRenamed, A.LocationArchived, A.LocationPhotoChanged, A.PermissionChanged,
      A.SettingsChanged, A.StatusChanged, A.AlertSent]).toEqual([1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15]);
  });

  describe('asUtcIso (E-16)', () => {
    it('marks a time without an offset as UTC', () => {
      expect(asUtcIso('2026-09-28T12:05:00')).toBe('2026-09-28T12:05:00Z');
      expect(asUtcIso('2026-09-28T12:05:00.1234567')).toBe('2026-09-28T12:05:00.1234567Z');
    });

    it('leaves a time that already ends in Z alone', () => {
      expect(asUtcIso('2026-09-28T12:05:00Z')).toBe('2026-09-28T12:05:00Z');
    });

    it('leaves a time with an explicit offset alone', () => {
      expect(asUtcIso('2026-09-28T14:05:00+02:00')).toBe('2026-09-28T14:05:00+02:00');
      expect(asUtcIso('2026-09-28T10:05:00-02:00')).toBe('2026-09-28T10:05:00-02:00');
    });

    it('gives the same instant either way', () => {
      expect(new Date(asUtcIso('2026-09-28T12:05:00')).getTime())
        .toBe(new Date(asUtcIso('2026-09-28T14:05:00+02:00')).getTime());
    });
  });

  describe('formatHistoryDetails (E-13: the Excel text, ChemicalHistoryDetailsFormatterTests)', () => {
    it.each(VECTORS)('%s', (_name, partial, expected) => {
      expect(formatHistoryDetails(event(partial), t)).toBe(expected);
    });

    it('covers every action', () => {
      const covered = new Set(VECTORS.map(([, partial]) => partial.action));
      expect(CHEMICAL_HISTORY_ACTIONS.filter((action) => !covered.has(action))).toEqual([]);
    });

    it('formats numbers like C# "0.###" with the invariant culture', () => {
      expect(formatHistoryNumber(2)).toBe('2');
      expect(formatHistoryNumber(2.5)).toBe('2.5');
      expect(formatHistoryNumber(-0.125)).toBe('-0.125');
      expect(formatHistoryNumber(1000000)).toBe('1000000');
      // A difference of two 3-decimal amounts, with the float error rounded away.
      expect(formatHistoryNumber(0.3 - 0.1)).toBe('0.2');
    });

    it('maps each C# text to one web key', () => {
      // The C# ChemicalLocalizationKeys names the formatter uses, and the web keys with the same words.
      expect(HISTORY_TEXT_KEYS).toEqual({
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
      });
      expect(new Set(Object.values(HISTORY_TEXT_KEYS)).size).toBe(Object.keys(HISTORY_TEXT_KEYS).length);
    });

    it('translates through the web keys', () => {
      const web = (key: string) => `[${key}]`;

      expect(formatHistoryDetails(event({action: A.Removed, removalReason: ChemicalRemovalReason.Used, note: 'tom'}), web))
        .toBe('[Chemicals: History reason]: [Chemicals: Reason used] · tom');
      expect(formatHistoryDetails(event({action: A.AlertSent, threshold: ChemicalAlertThreshold.OneMonth, deadlineKind: ChemicalDeadlineKind.Sales}), web))
        .toBe('[Chemicals: History threshold one month] ([Chemicals: Sales deadline]) · push 0 · e-mail 0');
    });
  });
});
