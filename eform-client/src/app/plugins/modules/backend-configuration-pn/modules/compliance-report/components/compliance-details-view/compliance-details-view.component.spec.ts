import {ComponentFixture, TestBed} from '@angular/core/testing';
import {NO_ERRORS_SCHEMA} from '@angular/core';
import {MatDialog} from '@angular/material/dialog';
import {TranslateModule} from '@ngx-translate/core';
import {Subject, of} from 'rxjs';
import {ComplianceReportRowModel} from '../../../../models';
import {
  BackendConfigurationPnComplianceReportService,
  BackendConfigurationPnCompliancesService,
} from '../../../../services';
import {ComplianceReportStateService} from '../../store';
import {ComplianceDetailsViewComponent} from './compliance-details-view.component';

/**
 * #1300 — on Detaljer an UNCOMPLETED task dated after today (Copenhagen date)
 * can be neither filled in (row click → complete modal) nor deleted. Today's
 * and past rows keep both actions; completed rows keep none (status quo).
 *
 * The clock is pinned with fake timers at 2026-09-18 10:00 UTC (12:00 in
 * Copenhagen), so "today" is 2026-09-18 in every cell below.
 */
describe('ComplianceDetailsViewComponent — future tasks (#1300)', () => {
  let fixture: ComponentFixture<ComplianceDetailsViewComponent>;
  let component: ComplianceDetailsViewComponent;
  let state: ComplianceReportStateService;
  let index: jest.Mock;
  let dialogOpen: jest.Mock;
  let deleteCompliance: jest.Mock;

  const row = (
    complianceId: number,
    taskDate: string,
    completed = false,
    areaRulePlanningId: number | null = 7,
  ): ComplianceReportRowModel => ({
    complianceId,
    taskDate,
    startHour: 9,
    duration: 1,
    isAllDay: false,
    title: `Opgave ${complianceId}`,
    propertyId: 5,
    propertyName: 'Ejendom A',
    boardId: null,
    boardName: '',
    tags: [],
    workerNames: [],
    workerSiteIds: [],
    teamAssigneeIds: [],
    completed,
    doneAt: null,
    sdkCaseId: 100 + complianceId,
    eformId: 9,
    planningId: 3,
    areaRulePlanningId,
    checkListId: 9,
  });

  const render = (rows: ComplianceReportRowModel[]) => {
    index.mockReturnValue(of({success: true, model: {total: rows.length, entities: rows}}));
    state.requestFetch();
    fixture.detectChanges();
  };

  const rowEl = (complianceId: number): HTMLElement =>
    fixture.nativeElement.querySelector(`[data-compliance-id="${complianceId}"]`);

  const deleteButton = (complianceId: number) =>
    rowEl(complianceId)?.querySelector('.compliance-details__delete') ?? null;

  beforeEach(async () => {
    jest.useFakeTimers();
    jest.setSystemTime(new Date('2026-09-18T10:00:00Z'));
    index = jest.fn().mockReturnValue(of({success: true, model: {total: 0, entities: []}}));
    dialogOpen = jest.fn(() => ({afterClosed: () => new Subject<unknown>()}));
    deleteCompliance = jest.fn().mockReturnValue(of({success: true}));

    await TestBed.configureTestingModule({
      declarations: [ComplianceDetailsViewComponent],
      imports: [TranslateModule.forRoot()],
      providers: [
        ComplianceReportStateService,
        {provide: BackendConfigurationPnComplianceReportService, useValue: {index}},
        {provide: BackendConfigurationPnCompliancesService, useValue: {deleteCompliance}},
        {provide: MatDialog, useValue: {open: dialogOpen}},
      ],
      schemas: [NO_ERRORS_SCHEMA],
    }).compileComponents();

    fixture = TestBed.createComponent(ComplianceDetailsViewComponent);
    component = fixture.componentInstance;
    // The page passes the admin role in; these cases run as an admin.
    component.canDeleteLogs = true;
    state = TestBed.inject(ComplianceReportStateService);
    state.setMode('details');
    fixture.detectChanges();
  });

  afterEach(() => {
    jest.useRealTimers();
  });

  describe('isRowCompletable', () => {
    it.each([
      ['yesterday, open', '2026-09-17', false, true],
      ['today, open', '2026-09-18', false, true],
      ['tomorrow, open', '2026-09-19', false, false],
      ['next year, open', '2027-09-18', false, false],
      ['yesterday, completed', '2026-09-17', true, false],
      ['tomorrow, completed', '2026-09-19', true, false],
    ])('%s → %s', (_label, taskDate, completed, expected) => {
      expect(component.isRowCompletable(row(1, taskDate as string, completed as boolean))).toBe(expected);
    });

    it('still requires an area-rule planning (status quo)', () => {
      expect(component.isRowCompletable(row(1, '2026-09-18', false, null))).toBe(false);
    });
  });

  describe('canDeleteRow', () => {
    it.each([
      ['yesterday, open', '2026-09-17', false, true],
      ['today, open', '2026-09-18', false, true],
      ['tomorrow, open', '2026-09-19', false, false],
      ['completed, past (status quo: no delete in Detaljer)', '2026-09-17', true, false],
    ])('%s → %s', (_label, taskDate, completed, expected) => {
      expect(component.canDeleteRow(row(1, taskDate as string, completed as boolean))).toBe(expected);
    });
  });

  it('renders no delete button and no click target on an uncompleted FUTURE row, but does on today\'s', () => {
    render([row(1, '2026-09-19'), row(2, '2026-09-18'), row(3, '2026-09-17')]);

    expect(deleteButton(1)).toBeNull();
    expect(rowEl(1).classList).not.toContain('is-clickable');
    expect(rowEl(1).getAttribute('tabindex')).toBeNull();
    expect(rowEl(1).classList).toContain('is-future');

    expect(deleteButton(2)).not.toBeNull();
    expect(rowEl(2).classList).toContain('is-clickable');
    expect(rowEl(2).getAttribute('tabindex')).toBe('0');

    expect(deleteButton(3)).not.toBeNull();
  });

  it('renders no delete button for a non-admin', () => {
    component.canDeleteLogs = false;
    render([row(2, '2026-09-18'), row(3, '2026-09-17')]);

    expect(component.canDeleteRow(row(2, '2026-09-18'))).toBe(false);
    expect(deleteButton(2)).toBeNull();
    expect(deleteButton(3)).toBeNull();
  });

  it('does not open the complete modal for a future row', () => {
    component.onRowClicked(row(1, '2026-09-19'));
    expect(dialogOpen).not.toHaveBeenCalled();
  });

  it('opens the complete modal for today\'s row with the compliance-page source', () => {
    component.onRowClicked(row(2, '2026-09-18'));
    expect(dialogOpen).toHaveBeenCalledTimes(1);
    const config = dialogOpen.mock.calls[0][1];
    expect(config.data.source).toBe('compliance');
    expect(config.data.occurrenceDate).toBe('2026-09-18');
  });

  it('refuses to open the delete confirm for a future row even if called directly', () => {
    const event = {stopPropagation: jest.fn(), currentTarget: document.createElement('button')} as unknown as MouseEvent;
    component.openDeleteConfirm(row(1, '2026-09-19'), event);
    component.confirmDelete();
    expect(deleteCompliance).not.toHaveBeenCalled();
  });

  describe('planned occurrences (#1332)', () => {
    const planned = (planningId: number, taskDate: string): ComplianceReportRowModel => ({
      ...row(0, taskDate),
      sdkCaseId: 0,
      checkListId: null,
      planningId,
      isProjected: true,
    });

    it('asks the server for planned rows', () => {
      render([]);
      expect(index).toHaveBeenLastCalledWith(expect.objectContaining({includeProjected: true}));
    });

    it('is never completable or deletable, even dated today in the browser', () => {
      // A page left open past midnight: a row planned for "tomorrow" is now dated today.
      const today = planned(3, '2026-09-18');
      expect(component.isRowCompletable(today)).toBe(false);
      expect(component.canDeleteRow(today)).toBe(false);
      expect(component.isFutureUncompleted(today)).toBe(true);
      component.onRowClicked(today);
      expect(dialogOpen).not.toHaveBeenCalled();
    });

    it('renders read-only with a "Planned" chip and no compliance id', () => {
      render([planned(3, '2026-11-30'), planned(4, '2026-11-30'), row(2, '2026-09-18')]);

      const rows = fixture.nativeElement.querySelectorAll('[data-planned]') as NodeListOf<HTMLElement>;
      // Both planned rows render although they share complianceId 0.
      expect(rows.length).toBe(2);
      rows.forEach((el) => {
        expect(el.getAttribute('data-compliance-id')).toBeNull();
        expect(el.classList).not.toContain('is-clickable');
        expect(el.classList).toContain('is-future');
        expect(el.getAttribute('tabindex')).toBeNull();
        expect(el.querySelector('.compliance-details__delete')).toBeNull();
        expect(el.querySelector('.compliance-details__planned')?.textContent?.trim()).toBe('Planned');
      });

      // A deployed row carries no chip.
      expect(rowEl(2).querySelector('.compliance-details__planned')).toBeNull();
      expect(deleteButton(2)).not.toBeNull();
    });

    it('keys planned rows by planning, date and position, deployed rows by compliance id', () => {
      // Same planning and date twice: an occurrence moved onto another rule date.
      expect(component.trackByRow(0, planned(3, '2026-11-30'))).toBe('planned:3:2026-11-30:0');
      expect(component.trackByRow(1, planned(3, '2026-11-30'))).toBe('planned:3:2026-11-30:1');
      expect(component.trackByRow(2, row(2, '2026-09-18'))).toBe('compliance:2');
    });

    it('labels the status circle "Planned" for a planned row', () => {
      expect(component.statusLabelKey(planned(3, '2026-11-30'))).toBe('Planned');
      expect(component.statusLabelKey(row(2, '2026-09-18', true))).toBe('Task done');
      expect(component.statusLabelKey(row(2, '2026-09-18'))).toBe('Task not done');
    });
  });

  it('flips at Copenhagen midnight, not UTC midnight (CEST: 22:00 UTC)', () => {
    const tomorrow = row(1, '2026-09-19');
    jest.setSystemTime(new Date('2026-09-18T21:59:00Z')); // 23:59 in Copenhagen
    expect(component.isRowCompletable(tomorrow)).toBe(false);
    jest.setSystemTime(new Date('2026-09-18T22:00:00Z')); // 00:00 in Copenhagen, still the 18th in UTC
    expect(component.isRowCompletable(tomorrow)).toBe(true);
    expect(component.canDeleteRow(tomorrow)).toBe(true);
  });
});
