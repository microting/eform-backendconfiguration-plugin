import {describe, it, expect, beforeEach, afterEach, vi} from 'vitest';
vi.mock('ng-gallery', () => ({Gallery: class {}, GalleryModule: class {}, ImageItem: class {}, GALLERY_CONFIG: 'GALLERY_CONFIG'}));
vi.mock('ng-gallery/lightbox', () => ({Lightbox: class {}, LightboxModule: class {}}));
vi.mock('ng2-pdf-viewer', () => ({PdfViewerModule: class {}, PdfViewerComponent: class {}}));
import {NO_ERRORS_SCHEMA} from '@angular/core';
import {ComponentFixture, TestBed} from '@angular/core/testing';
import {MatDialog} from '@angular/material/dialog';
import {TranslateModule} from '@ngx-translate/core';
import {Store} from '@ngrx/store';
import {of} from 'rxjs';
import {BackendConfigurationPnComplianceReportService} from '../../../../services';
import {ComplianceReportStateService} from '../../store';
import {ComplianceReportPageComponent} from './compliance-report-page.component';
import {CommonModule} from '@angular/common';

/**
 * The Oversigt/Detaljer/Rapport switcher. Since #5562 it is the workspace
 * `.segmented` control (outlined, the checked option on the light primary
 * container) in place of the stock `mat-button-toggle-group` of #1296, still
 * between the filter bar and the result card. It is a radiogroup of plain
 * buttons: a click (also on the ALREADY-checked Oversigt, which must
 * re-fetch, #1296) switches the view, and so do the arrow keys (#1348).
 * Since #1299 Oversigt no longer resets any filter.
 *
 * The filter bar, the card and the three views are unknown elements under
 * NO_ERRORS_SCHEMA. The state service is the REAL one (it has no
 * dependencies), so `mode` and the reset are the actual implementation.
 */
describe('ComplianceReportPageComponent — mode switcher', () => {
  let fixture: ComponentFixture<ComplianceReportPageComponent>;
  let state: ComplianceReportStateService;

  const MODES = ['overview', 'details', 'report'] as const;

  const switcher = (): HTMLElement =>
    fixture.nativeElement.querySelector('.compliance-mode-toggle') as HTMLElement;

  const option = (mode: string): HTMLButtonElement =>
    fixture.nativeElement.querySelector(`#complianceMode-${mode}`) as HTMLButtonElement;

  /** Clicks the option and re-renders. */
  const click = (mode: string): void => {
    option(mode).click();
    fixture.detectChanges();
  };

  /** Presses a key on the option, as a keyboard user with focus on it does. */
  const press = (mode: string, key: string): KeyboardEvent => {
    const event = new KeyboardEvent('keydown', {key, bubbles: true, cancelable: true});
    option(mode).dispatchEvent(event);
    fixture.detectChanges();
    return event;
  };

  const expectChecked = (checked: string): void => {
    for (const m of MODES) {
      expect(option(m).getAttribute('aria-checked')).toBe(String(m === checked));
      expect(option(m).classList.contains('active')).toBe(m === checked);
      // Roving tabindex: the checked option is the group's only tab stop.
      expect(option(m).getAttribute('tabindex')).toBe(m === checked ? '0' : '-1');
    }
  };

  const USER_ID = 7;

  beforeEach(() => {
    window.localStorage.clear();
    state = new ComplianceReportStateService();

    TestBed.configureTestingModule({
      imports: [ComplianceReportPageComponent, TranslateModule.forRoot()],
      providers: [
        {provide: ComplianceReportStateService, useValue: state},
        {provide: BackendConfigurationPnComplianceReportService, useValue: {export: vi.fn()}},
        {provide: MatDialog, useValue: {open: vi.fn()}},
        {provide: Store, useValue: {select: vi.fn().mockReturnValue(of({id: USER_ID}))}},
      ],
      schemas: [NO_ERRORS_SCHEMA],
    }).overrideComponent(ComplianceReportPageComponent, {set: {imports: [CommonModule, TranslateModule], schemas: [NO_ERRORS_SCHEMA]}});

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

  it('is the workspace .segmented radiogroup, not a mat-button-toggle-group', () => {
    expect(fixture.nativeElement.querySelector('mat-button-toggle-group')).toBeNull();
    expect(fixture.nativeElement.querySelector('mat-button-toggle')).toBeNull();
    expect(switcher()).toBeTruthy();
    expect(switcher().classList).toContain('segmented');
    expect(switcher().getAttribute('role')).toBe('radiogroup');
  });

  it('sits between the filter bar and the result card, as before', () => {
    const children = Array.from(fixture.nativeElement.querySelector('.compliance-page').children) as Element[];
    const filters = children.findIndex((el) => el.tagName.toLowerCase() === 'app-compliance-report-filters');
    const toggle = children.indexOf(switcher());
    const card = children.findIndex((el) => el.id === 'complianceCasesRoot');
    expect(filters).toBeGreaterThanOrEqual(0);
    expect(toggle).toBe(filters + 1);
    expect(card).toBe(toggle + 1);
  });

  it('renders one .segmented-option button per mode, keeping the complianceMode-<mode> ids', () => {
    const options = Array.from(switcher().querySelectorAll('.segmented-option')) as HTMLElement[];
    expect(options.map((o) => o.id)).toEqual(MODES.map((m) => `complianceMode-${m}`));
    for (const o of options) {
      expect(o.tagName.toLowerCase()).toBe('button');
      expect(o.getAttribute('type')).toBe('button');
      expect(o.getAttribute('role')).toBe('radio');
    }
  });

  it('marks only the active mode as checked: aria-checked, the active class and the tab stop', () => {
    expect(state.mode).toBe('overview');
    expectChecked('overview');
  });

  it('clicking Detaljer switches the mode without resetting', () => {
    const reset = vi.spyOn(state, 'resetToOverview');
    const setMode = vi.spyOn(state, 'setMode');

    click('details');

    expect(setMode).toHaveBeenCalledWith('details');
    expect(reset).not.toHaveBeenCalled();
    expect(state.mode).toBe('details');
    expectChecked('details');
  });

  it('clicking Rapport switches the mode without resetting', () => {
    const reset = vi.spyOn(state, 'resetToOverview');

    click('report');

    expect(reset).not.toHaveBeenCalled();
    expect(state.mode).toBe('report');
    expectChecked('report');
  });

  it('clicking the ALREADY-selected Oversigt re-fetches and keeps every filter (#1296, #1299)', () => {
    expect(state.mode).toBe('overview');
    state.setFilter(NON_DEFAULT);
    const toOverview = vi.spyOn(state, 'resetToOverview');
    const fetches: number[] = [];
    state.fetchRequested$.subscribe(() => fetches.push(1));
    const before = fetches.length;

    click('overview');

    expect(toOverview).toHaveBeenCalledTimes(1);
    expect(fetches.length).toBe(before + 1);
    expect(state.filters).toEqual(NON_DEFAULT);
    expect(state.mode).toBe('overview');
    expectChecked('overview');
  });

  it('clicking Oversigt from Detaljer re-checks Oversigt and resets NO filter (#1299)', () => {
    click('details');
    state.setFilter(NON_DEFAULT);

    click('overview');

    expect(state.filters).toEqual(NON_DEFAULT);
    expect(state.mode).toBe('overview');
    expectChecked('overview');
  });

  it('clicking Oversigt from Rapport resets NO filter either (#1299)', () => {
    click('report');
    state.setFilter(NON_DEFAULT);

    click('overview');

    expect(state.filters).toEqual(NON_DEFAULT);
    expect(state.mode).toBe('overview');
  });

  it('ArrowRight from Oversigt switches the view to Detaljer, then to Rapport, and focus follows (#1348)', () => {
    const setMode = vi.spyOn(state, 'setMode');

    const first = press('overview', 'ArrowRight');

    expect(first.defaultPrevented).toBe(true);
    expect(setMode).toHaveBeenCalledTimes(1);
    expect(setMode).toHaveBeenCalledWith('details');
    expect(state.mode).toBe('details');
    expectChecked('details');
    expect(document.activeElement).toBe(option('details'));

    press('details', 'ArrowRight');

    expect(setMode).toHaveBeenCalledTimes(2);
    expect(setMode).toHaveBeenLastCalledWith('report');
    expect(state.mode).toBe('report');
    expectChecked('report');
    expect(document.activeElement).toBe(option('report'));
  });

  it('ArrowLeft back onto Oversigt fetches Oversigt once (#1348)', () => {
    click('details');
    const toOverview = vi.spyOn(state, 'resetToOverview');
    const fetches: number[] = [];
    state.fetchRequested$.subscribe(() => fetches.push(1));
    const before = fetches.length;

    press('details', 'ArrowLeft');

    expect(toOverview).toHaveBeenCalledTimes(1);
    expect(fetches.length).toBe(before + 1);
    expect(state.mode).toBe('overview');
    expect(document.activeElement).toBe(option('overview'));
  });

  it('the arrow keys wrap at both ends, and Home/End jump to the first/last mode', () => {
    press('overview', 'ArrowLeft');
    expect(state.mode).toBe('report');

    press('report', 'ArrowRight');
    expect(state.mode).toBe('overview');

    press('overview', 'End');
    expect(state.mode).toBe('report');

    press('report', 'Home');
    expect(state.mode).toBe('overview');
  });

  it('Home on Oversigt and End on Rapport, already checked, switch nothing', () => {
    click('report');
    const setMode = vi.spyOn(state, 'setMode');
    const toOverview = vi.spyOn(state, 'resetToOverview');

    const end = press('report', 'End');

    expect(end.defaultPrevented).toBe(true);
    expect(setMode).not.toHaveBeenCalled();
    expect(state.mode).toBe('report');
    expect(document.activeElement).toBe(option('report'));

    press('report', 'Home');
    expect(state.mode).toBe('overview');
    expect(toOverview).toHaveBeenCalledTimes(1);

    press('overview', 'Home');
    expect(toOverview).toHaveBeenCalledTimes(1);
    expect(state.mode).toBe('overview');
  });

  it('other keys leave the mode and the event alone', () => {
    const setMode = vi.spyOn(state, 'setMode');
    const toOverview = vi.spyOn(state, 'resetToOverview');

    const event = press('overview', 'a');

    expect(event.defaultPrevented).toBe(false);
    expect(setMode).not.toHaveBeenCalled();
    expect(toOverview).not.toHaveBeenCalled();
    expect(state.mode).toBe('overview');
  });

  it('one mouse click on another mode switches exactly once, never twice (#1348)', () => {
    const setMode = vi.spyOn(state, 'setMode');
    const toOverview = vi.spyOn(state, 'resetToOverview');

    click('details');
    expect(setMode).toHaveBeenCalledTimes(1);

    const fetches: number[] = [];
    state.fetchRequested$.subscribe(() => fetches.push(1));
    const before = fetches.length;

    click('overview');
    expect(toOverview).toHaveBeenCalledTimes(1);
    expect(fetches.length).toBe(before + 1);
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
      imports: [ComplianceReportPageComponent, TranslateModule.forRoot()],
      providers: [
        {provide: ComplianceReportStateService, useValue: state},
        {provide: BackendConfigurationPnComplianceReportService, useValue: {export: vi.fn()}},
        {provide: MatDialog, useValue: {open: vi.fn()}},
        {provide: Store, useValue: {select: vi.fn().mockReturnValue(of(userId == null ? null : {id: userId}))}},
      ],
      schemas: [NO_ERRORS_SCHEMA],
    }).overrideComponent(ComplianceReportPageComponent, {set: {imports: [CommonModule, TranslateModule], schemas: [NO_ERRORS_SCHEMA]}});
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
    const restore = vi.spyOn(state, 'restoreSavedPeriod');
    const enter = vi.spyOn(state, 'enterPage');

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
