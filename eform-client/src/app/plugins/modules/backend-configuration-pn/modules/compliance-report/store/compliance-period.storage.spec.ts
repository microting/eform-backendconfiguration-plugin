import {
  compliancePeriodStorageKey,
  parseSavedComplianceDate,
  readSavedCompliancePeriod,
  writeSavedCompliancePeriod,
} from './compliance-period.storage';

describe('compliance-period.storage (#1299)', () => {
  const USER = 7;
  const KEY = 'bcpn.compliance.period.7';

  beforeEach(() => window.localStorage.clear());
  afterEach(() => {
    jest.restoreAllMocks();
    window.localStorage.clear();
  });

  it('keys the period per user', () => {
    expect(compliancePeriodStorageKey(USER)).toBe(KEY);
  });

  it('saves a preset without dates', () => {
    writeSavedCompliancePeriod(USER, {periodPreset: 'ytd1y', customFrom: new Date(2026, 0, 2), customTo: null});

    expect(JSON.parse(window.localStorage.getItem(KEY))).toEqual({
      periodPreset: 'ytd1y', customFrom: null, customTo: null,
    });
  });

  it('saves a committed custom range as plain local dates', () => {
    writeSavedCompliancePeriod(USER, {
      periodPreset: 'custom', customFrom: new Date(2026, 1, 3, 23, 30), customTo: new Date(2026, 4, 20),
    });

    expect(JSON.parse(window.localStorage.getItem(KEY))).toEqual({
      periodPreset: 'custom', customFrom: '2026-02-03', customTo: '2026-05-20',
    });
  });

  it.each([
    ['no dates', null, null],
    ['no end', new Date(2026, 1, 3), null],
    ['no start', null, new Date(2026, 1, 3)],
    ['a backwards range', new Date(2026, 4, 20), new Date(2026, 1, 3)],
    ['an invalid date', new Date('nope'), new Date(2026, 1, 3)],
  ])('does not save an uncommitted/unusable custom range (%s)', (_label, from, to) => {
    writeSavedCompliancePeriod(USER, {periodPreset: 'custom', customFrom: from, customTo: to});

    expect(window.localStorage.getItem(KEY)).toBeNull();
  });

  it('a same-day custom range is valid', () => {
    writeSavedCompliancePeriod(USER, {
      periodPreset: 'custom', customFrom: new Date(2026, 1, 3), customTo: new Date(2026, 1, 3),
    });

    expect(readSavedCompliancePeriod(USER).customTo.getTime()).toBe(new Date(2026, 1, 3).getTime());
  });

  it.each(['1', '3', '6', '12', 'ytd', 'ytd1y'] as const)('round-trips the %s preset', (preset) => {
    writeSavedCompliancePeriod(USER, {periodPreset: preset, customFrom: null, customTo: null});

    expect(readSavedCompliancePeriod(USER)).toEqual({periodPreset: preset, customFrom: null, customTo: null});
  });

  it('round-trips a custom range to local-midnight dates', () => {
    writeSavedCompliancePeriod(USER, {
      periodPreset: 'custom', customFrom: new Date(2024, 1, 29), customTo: new Date(2026, 11, 31),
    });

    const restored = readSavedCompliancePeriod(USER);
    expect(restored.periodPreset).toBe('custom');
    expect(restored.customFrom.getTime()).toBe(new Date(2024, 1, 29).getTime());
    expect(restored.customTo.getTime()).toBe(new Date(2026, 11, 31).getTime());
  });

  it('never reads another user\'s period', () => {
    writeSavedCompliancePeriod(USER, {periodPreset: '3', customFrom: null, customTo: null});

    expect(readSavedCompliancePeriod(8)).toBeNull();
  });

  it('does not save or read without a valid user id', () => {
    writeSavedCompliancePeriod(0, {periodPreset: '3', customFrom: null, customTo: null});
    writeSavedCompliancePeriod(undefined, {periodPreset: '3', customFrom: null, customTo: null});
    expect(window.localStorage.length).toBe(0);
    expect(readSavedCompliancePeriod(0)).toBeNull();
    expect(readSavedCompliancePeriod(1.5)).toBeNull();
  });

  it('returns null when nothing is saved', () => {
    expect(readSavedCompliancePeriod(USER)).toBeNull();
  });

  it.each([
    ['not JSON', '{nope'],
    ['JSON null', 'null'],
    ['a number', '42'],
    ['no preset', JSON.stringify({customFrom: null})],
    ['an unknown preset', JSON.stringify({periodPreset: '24'})],
    ['a numeric preset', JSON.stringify({periodPreset: 3})],
    ['custom without dates', JSON.stringify({periodPreset: 'custom', customFrom: null, customTo: null})],
    ['custom with one date', JSON.stringify({periodPreset: 'custom', customFrom: '2026-02-03', customTo: null})],
    ['custom backwards', JSON.stringify({periodPreset: 'custom', customFrom: '2026-05-20', customTo: '2026-02-03'})],
    ['custom with a non-existent day', JSON.stringify({periodPreset: 'custom', customFrom: '2026-02-31', customTo: '2026-03-05'})],
    ['custom with a timestamp', JSON.stringify({periodPreset: 'custom', customFrom: '2026-02-03T00:00:00Z', customTo: '2026-03-05'})],
  ])('returns null for a corrupt payload (%s)', (_label, raw) => {
    window.localStorage.setItem(KEY, raw);

    expect(readSavedCompliancePeriod(USER)).toBeNull();
  });

  it('ignores stray dates saved next to a fixed preset', () => {
    window.localStorage.setItem(KEY, JSON.stringify({periodPreset: '6', customFrom: '2026-02-03', customTo: '2026-03-05'}));

    expect(readSavedCompliancePeriod(USER)).toEqual({periodPreset: '6', customFrom: null, customTo: null});
  });

  it.each([
    ['2024-02-29', new Date(2024, 1, 29)],
    ['2025-02-29', null],
    ['2026-12-31', new Date(2026, 11, 31)],
    ['2026-01-01', new Date(2026, 0, 1)],
    ['2026-13-01', null],
    ['2026-10-00', null],
    [20261005, null],
  ])('parseSavedComplianceDate(%s)', (input, expected) => {
    const parsed = parseSavedComplianceDate(input);
    expect(parsed === null ? null : parsed.getTime()).toBe(expected === null ? null : expected.getTime());
  });

  it('does not throw when reading from storage throws', () => {
    jest.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new Error('SecurityError');
    });

    expect(() => readSavedCompliancePeriod(USER)).not.toThrow();
    expect(readSavedCompliancePeriod(USER)).toBeNull();
  });

  it('does not throw when writing to storage throws', () => {
    jest.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('QuotaExceededError');
    });

    expect(() =>
      writeSavedCompliancePeriod(USER, {periodPreset: '3', customFrom: null, customTo: null})
    ).not.toThrow();
  });
});
