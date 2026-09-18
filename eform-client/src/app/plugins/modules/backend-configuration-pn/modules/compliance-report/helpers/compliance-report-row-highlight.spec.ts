import {
  ComplianceReportRowIdentity,
  complianceReportRowKey,
  nextComplianceReportRowKey,
} from './compliance-report-row-highlight';

/**
 * #1290 — after "Slet log" the Rapport view lands on the NEXT log. These pin
 * which row that is, and the identity that survives the re-fetch.
 */
describe('complianceReportRowKey', () => {
  it.each([
    ['a row with an SDK case is keyed by the case', {complianceId: 7, sdkCaseId: 42}, 'case:42'],
    ['a row without a case is keyed by the compliance', {complianceId: 7, sdkCaseId: 0}, 'compliance:7'],
    ['a negative/absent case id counts as no case', {complianceId: 7, sdkCaseId: -1}, 'compliance:7'],
  ])('%s', (_, row: ComplianceReportRowIdentity, expected: string) => {
    expect(complianceReportRowKey(row)).toBe(expected);
  });

  it('never collides a case id with a compliance id of the same number', () => {
    expect(complianceReportRowKey({complianceId: 5, sdkCaseId: 0})).not.toBe(
      complianceReportRowKey({complianceId: 9, sdkCaseId: 5}),
    );
  });
});

describe('nextComplianceReportRowKey', () => {
  const row = (complianceId: number, sdkCaseId = 100 + complianceId): ComplianceReportRowIdentity => ({
    complianceId,
    sdkCaseId,
  });
  const rows = [row(1), row(2), row(3), row(4, 0)];

  it.each([
    ['first row → the second', row(1), 'case:102'],
    ['a middle row → the one after it', row(2), 'case:103'],
    ['the row before a case-less row → the case-less row, by compliance id', row(3), 'compliance:4'],
    ['the LAST row → the one before it', row(4, 0), 'case:103'],
  ])('%s', (_, deleted: ComplianceReportRowIdentity, expected: string) => {
    expect(nextComplianceReportRowKey(rows, deleted)).toBe(expected);
  });

  it('the only row → null (nothing left to land on)', () => {
    expect(nextComplianceReportRowKey([row(1)], row(1))).toBeNull();
  });

  it('a row that is not on the page → null', () => {
    expect(nextComplianceReportRowKey(rows, row(99))).toBeNull();
  });

  it('an empty page → null', () => {
    expect(nextComplianceReportRowKey([], row(1))).toBeNull();
  });

  it('matches the deleted row by KEY, not by object identity', () => {
    // The row the dialog was opened from and the row in the list are
    // different objects after any re-render.
    expect(nextComplianceReportRowKey(rows, {complianceId: 2, sdkCaseId: 102})).toBe('case:103');
  });
});
