import {NO_ERRORS_SCHEMA} from '@angular/core';
import {ComponentFixture, TestBed} from '@angular/core/testing';
import {By} from '@angular/platform-browser';
import {ActivatedRoute, Router} from '@angular/router';
import {MatButtonToggle, MatButtonToggleGroup, MatButtonToggleModule} from '@angular/material/button-toggle';
import {MatDialog} from '@angular/material/dialog';
import {TranslateModule} from '@ngx-translate/core';
import {Store} from '@ngrx/store';
import {of} from 'rxjs';
import {BackendConfigurationPnComplianceReportService} from '../../../../services';
import {ComplianceReportStateService} from '../../store';
import {ComplianceReportPageComponent} from './compliance-report-page.component';

/**
 * The Oversigt/Detaljer/Rapport switcher (#1296). It used to be three
 * hand-rolled `<button aria-pressed>`s; it is now a stock
 * `mat-button-toggle-group` with the checkmark indicator, bound with
 * `(click)` on each toggle so that clicking the ALREADY-selected Oversigt
 * still re-fetches Oversigt (`(change)` does not fire on a re-click). Since
 * #1299 that click no longer resets any filter.
 *
 * Rendered through TestBed with only MatButtonToggleModule real; the filter
 * bar, the card and the three views are unknown elements under
 * NO_ERRORS_SCHEMA. The state service is the REAL one (it has no
 * dependencies), so `mode` and the reset are the actual implementation.
 */
describe('ComplianceReportPageComponent — mode switcher', () => {
  let fixture: ComponentFixture<ComplianceReportPageComponent>;
  let state: ComplianceReportStateService;

  const MODES = ['overview', 'details', 'report'] as const;

  const group = (): MatButtonToggleGroup =>
    fixture.debugElement.query(By.directive(MatButtonToggleGroup))?.injector.get(MatButtonToggleGroup);

  const toggleHost = (mode: string): HTMLElement =>
    fixture.nativeElement.querySelector(`#complianceMode-${mode}`) as HTMLElement;

  const innerButton = (mode: string): HTMLButtonElement =>
    fixture.nativeElement.querySelector(`#complianceMode-${mode}-button`) as HTMLButtonElement;

  /** Clicks the inner `<button>` — what a real pointer hits — and re-renders. */
  const click = (mode: string): void => {
    innerButton(mode).click();
    fixture.detectChanges();
  };

  const USER_ID = 7;

  beforeEach(() => {
    window.localStorage.clear();
    state = new ComplianceReportStateService();

    TestBed.configureTestingModule({
      declarations: [ComplianceReportPageComponent],
      imports: [TranslateModule.forRoot(), MatButtonToggleModule],
      providers: [
        {provide: ComplianceReportStateService, useValue: state},
        {provide: BackendConfigurationPnComplianceReportService, useValue: {export: jest.fn()}},
        {provide: MatDialog, useValue: {open: jest.fn()}},
        {provide: ActivatedRoute, useValue: {snapshot: {queryParamMap: {get: () => null}}}},
        {provide: Router, useValue: {navigate: jest.fn().mockResolvedValue(true)}},
        {provide: Store, useValue: {select: jest.fn().mockReturnValue(of({id: USER_ID}))}},
      ],
      schemas: [NO_ERRORS_SCHEMA],
    });

    fixture = TestBed.createComponent(ComplianceReportPageComponent);
    fixture.detectChanges();
  });

  afterEach(() => {
    fixture.destroy();
    window.localStorage.clear();
  });

  /** A filter set far from every default, so a reset cannot go unnoticed. */
  const NON_DEFAULT = {
    propertyId: 4,
    boardIds: [2],
    tagIds: [1, 3],
    siteIds: [9],
    status: 'done' as const,
    periodPreset: 'ytd1y' as const,
    customFrom: null,
    customTo: null,
  };

  it('renders a mat-button-toggle-group whose value is the current mode', () => {
    expect(group()).toBeTruthy();
    expect(state.mode).toBe('overview');
    expect(group().value).toBe('overview');
  });

  it('renders one mat-button-toggle per mode, keeping the complianceMode-<mode> ids on the host', () => {
    const toggles = fixture.debugElement.queryAll(By.directive(MatButtonToggle));
    expect(toggles.map((t) => t.nativeElement.id)).toEqual(MODES.map((m) => `complianceMode-${m}`));
    for (const m of MODES) {
      expect(toggleHost(m).tagName.toLowerCase()).toBe('mat-button-toggle');
      // Material puts `<id>-button` on the inner button; e2e specs target it.
      expect(innerButton(m)).toBeTruthy();
    }
  });

  it('marks only the active mode as checked (aria-checked on the inner button, class on the host)', () => {
    expect(innerButton('overview').getAttribute('aria-checked')).toBe('true');
    expect(innerButton('details').getAttribute('aria-checked')).toBe('false');
    expect(innerButton('report').getAttribute('aria-checked')).toBe('false');
    expect(toggleHost('overview').classList).toContain('mat-button-toggle-checked');
    expect(toggleHost('details').classList).not.toContain('mat-button-toggle-checked');
    // The single-selection group is a radiogroup — the accepted #1296 trade-off.
    expect(fixture.nativeElement.querySelector('[aria-pressed]')).toBeNull();
  });

  it('shows the checkmark indicator (hideSingleSelectionIndicator stays false)', () => {
    expect(group().hideSingleSelectionIndicator).toBe(false);
    expect(toggleHost('overview').querySelector('.mat-pseudo-checkbox')).not.toBeNull();
  });

  it('clicking Detaljer switches the mode without resetting', () => {
    const reset = jest.spyOn(state, 'resetToOverview');
    const setMode = jest.spyOn(state, 'setMode');

    click('details');

    expect(setMode).toHaveBeenCalledWith('details');
    expect(reset).not.toHaveBeenCalled();
    expect(state.mode).toBe('details');
    expect(group().value).toBe('details');
    expect(toggleHost('details').classList).toContain('mat-button-toggle-checked');
    expect(innerButton('overview').getAttribute('aria-checked')).toBe('false');
  });

  it('clicking Rapport switches the mode without resetting', () => {
    const reset = jest.spyOn(state, 'resetToOverview');

    click('report');

    expect(reset).not.toHaveBeenCalled();
    expect(state.mode).toBe('report');
    expect(group().value).toBe('report');
  });

  it('clicking the ALREADY-selected Oversigt re-fetches and keeps every filter (#1299, via (click) not (change))', () => {
    expect(state.mode).toBe('overview');
    state.setFilter(NON_DEFAULT);
    const toOverview = jest.spyOn(state, 'resetToOverview');
    const fetches: number[] = [];
    state.fetchRequested$.subscribe(() => fetches.push(1));
    const before = fetches.length;

    click('overview');

    expect(toOverview).toHaveBeenCalledTimes(1);
    expect(fetches.length).toBe(before + 1);
    expect(state.filters).toEqual(NON_DEFAULT);
    expect(state.mode).toBe('overview');
    expect(group().value).toBe('overview');
  });

  it('clicking Oversigt from Detaljer re-checks Oversigt and resets NO filter (#1299)', () => {
    click('details');
    state.setFilter(NON_DEFAULT);

    click('overview');

    expect(state.filters).toEqual(NON_DEFAULT);
    expect(state.mode).toBe('overview');
    expect(group().value).toBe('overview');
    expect(innerButton('overview').getAttribute('aria-checked')).toBe('true');
    expect(innerButton('details').getAttribute('aria-checked')).toBe('false');
  });

  it('clicking Oversigt from Rapport resets NO filter either (#1299)', () => {
    click('report');
    state.setFilter(NON_DEFAULT);

    click('overview');

    expect(state.filters).toEqual(NON_DEFAULT);
    expect(state.mode).toBe('overview');
  });
});

/**
 * #1299: the page applies the signed-in user's saved period BEFORE its entry
 * fetch, so the very first Oversigt query already uses it.
 */
describe('ComplianceReportPageComponent — remembered period on entry', () => {
  const USER_ID = 7;

  function create(userId: number | null) {
    const state = new ComplianceReportStateService();
    TestBed.configureTestingModule({
      declarations: [ComplianceReportPageComponent],
      imports: [TranslateModule.forRoot(), MatButtonToggleModule],
      providers: [
        {provide: ComplianceReportStateService, useValue: state},
        {provide: BackendConfigurationPnComplianceReportService, useValue: {export: jest.fn()}},
        {provide: MatDialog, useValue: {open: jest.fn()}},
        {provide: ActivatedRoute, useValue: {snapshot: {queryParamMap: {get: () => null}}}},
        {provide: Router, useValue: {navigate: jest.fn().mockResolvedValue(true)}},
        {provide: Store, useValue: {select: jest.fn().mockReturnValue(of(userId == null ? null : {id: userId}))}},
      ],
      schemas: [NO_ERRORS_SCHEMA],
    });
    return state;
  }

  beforeEach(() => window.localStorage.clear());
  afterEach(() => window.localStorage.clear());

  it('restores the user\'s saved period and fetches Oversigt with it', () => {
    window.localStorage.setItem(
      `bcpn.compliance.period.${USER_ID}`,
      JSON.stringify({periodPreset: 'custom', customFrom: '2026-02-03', customTo: '2026-05-20'})
    );
    const state = create(USER_ID);
    // Both spies call through; only the order is observed.
    const restore = jest.spyOn(state, 'restoreSavedPeriod');
    const enter = jest.spyOn(state, 'enterPage');

    const fixture = TestBed.createComponent(ComplianceReportPageComponent);
    fixture.detectChanges();

    expect(restore).toHaveBeenCalledWith(USER_ID);
    expect(restore.mock.invocationCallOrder[0]).toBeLessThan(enter.mock.invocationCallOrder[0]);
    expect(state.filters.periodPreset).toBe('custom');
    expect(state.requestModel.dateFrom).toBe('2026-02-03');
    expect(state.requestModel.dateTo).toBe('2026-05-20');
    expect(state.reportVisible).toBe(true);
    fixture.destroy();
  });

  it('with no signed-in user it keeps the default and still enters the page', () => {
    const state = create(null);

    const fixture = TestBed.createComponent(ComplianceReportPageComponent);
    fixture.detectChanges();

    expect(state.filters.periodPreset).toBe('ytd');
    expect(state.reportVisible).toBe(true);
    fixture.destroy();
  });
});
