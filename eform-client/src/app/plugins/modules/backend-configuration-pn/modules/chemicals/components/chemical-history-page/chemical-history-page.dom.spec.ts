import {NO_ERRORS_SCHEMA} from '@angular/core';
import {ComponentFixture, TestBed} from '@angular/core/testing';
import {FormsModule} from '@angular/forms';
import {MatPaginatorModule} from '@angular/material/paginator';
import {NoopAnimationsModule} from '@angular/platform-browser/animations';
import {MtxSelectModule} from '@ng-matero/extensions/select';
import {Store} from '@ngrx/store';
import {TranslateModule} from '@ngx-translate/core';
import {ToastrService} from 'ngx-toastr';
import {BehaviorSubject, NEVER, Observable, of} from 'rxjs';
import {ChemicalHistoryAction, ChemicalHistoryEventModel, ChemicalRemovalReason, ChemicalStockUnit} from '../../../../models';
import {BackendConfigurationPnChemicalsService} from '../../../../services';
import {ChemicalHistoryFiltersModel, chemicalsInitialState} from '../../../../state';
import {asUtcIso} from '../../helpers/chemical-history.helper';
import {location} from '../../helpers/chemical-inventory.fixtures';
import {ChemicalHistoryPageComponent} from './chemical-history-page.component';

function historyEvent(partial: Partial<ChemicalHistoryEventModel>): ChemicalHistoryEventModel {
  return {
    at: '2026-09-28T12:05:00', userId: 5, userName: 'Mette Larsen', action: ChemicalHistoryAction.Registered,
    placementId: 1, chemicalId: 100, productName: 'Karate 2,5 WG', registrationNo: '1-212', locationId: 10, locationName: 'Lade',
    targetLocationId: null, targetLocationName: '', amount: null, unit: null, balanceAfter: null, removalReason: null,
    note: '', field: '', oldValue: '', newValue: '', status: null, statusText: '', threshold: null, deadlineKind: null,
    pushCount: 0, emailCount: 0, workerName: '', permissionsAdded: [], permissionsRemoved: [], ...partial,
  };
}

/** Template-level checks for W6: the ids the e2e specs use, the period guard, the load states and the columns. */
describe('ChemicalHistoryPageComponent (template)', () => {
  let fixture: ComponentFixture<ChemicalHistoryPageComponent>;
  let component: ChemicalHistoryPageComponent;
  let view$: BehaviorSubject<{propertyId: number | null; filters: ChemicalHistoryFiltersModel}>;
  const find = (selector: string): HTMLElement | null => fixture.nativeElement.querySelector(selector);
  const rows = (): HTMLElement[] => Array.from(fixture.nativeElement.querySelectorAll('#chemicalHistoryTable tbody tr'));
  const cell = (row: number, column: string) => rows()[row].querySelector(`.col-${column}`)?.textContent?.trim();

  const events = [
    historyEvent({at: '2026-09-29T07:12:00', userId: 0, userName: '', action: ChemicalHistoryAction.StatusChanged, statusText: 'Produkt afmeldt'}),
    historyEvent({at: '2026-09-28T12:05:00', action: ChemicalHistoryAction.Moved, targetLocationName: 'Kemirum', amount: 5, unit: ChemicalStockUnit.L}),
    // The same instant as the row above, sent with an offset (E-16).
    historyEvent({at: '2026-09-28T14:05:00+02:00', action: ChemicalHistoryAction.Removed, removalReason: ChemicalRemovalReason.Used, note: 'tom'}),
    historyEvent({at: '2026-09-28T12:05:00Z', action: ChemicalHistoryAction.LocationCreated, productName: '', locationName: ''}),
  ];

  let utc: jest.SpyInstance;

  function render(answer?: Observable<unknown>, exportAnswer?: Observable<unknown>, inventory?: Observable<unknown>): void {
    view$ = new BehaviorSubject({propertyId: 3 as number | null, filters: {...chemicalsInitialState.historyFilters}});
    TestBed.configureTestingModule({
      declarations: [ChemicalHistoryPageComponent],
      imports: [FormsModule, MtxSelectModule, MatPaginatorModule, NoopAnimationsModule, TranslateModule.forRoot()],
      providers: [
        {provide: Store, useValue: {
          select: jest.fn().mockReturnValue(view$),
          dispatch: jest.fn().mockImplementation((action) => view$.next({...view$.value, filters: action.payload})),
        }},
        {provide: BackendConfigurationPnChemicalsService, useValue: {
          getHistory: jest.fn().mockReturnValue(answer ?? of({success: true, model: {events, total: 4, users: [{userId: 5, name: 'Mette Larsen'}]}})),
          getPropertyInventory: jest.fn().mockReturnValue(inventory ?? of({success: true, model: {locations: [location(10, 'Lade', {propertyId: 3})]}})),
          exportHistory: jest.fn().mockReturnValue(exportAnswer ?? NEVER),
        }},
        {provide: ToastrService, useValue: {error: jest.fn()}},
      ],
      schemas: [NO_ERRORS_SCHEMA],
    });
    fixture = TestBed.createComponent(ChemicalHistoryPageComponent);
    component = fixture.componentInstance;
    component.now = () => new Date('2026-09-29T10:00:00Z');
    // Spied before the first render, so the template's own call is seen (m-1).
    utc = jest.spyOn(component, 'utc');
    fixture.detectChanges();
  }

  afterEach(() => fixture?.destroy());

  it('renders every control the e2e page object addresses', () => {
    render();

    for (const id of ['#chemicalHistoryPage', '#chemicalHistoryUserFilter', '#chemicalHistoryActionFilter', '#chemicalHistoryLocationFilter',
      '#chemicalHistoryFrom', '#chemicalHistoryTo', '#chemicalHistoryExportBtn', '#chemicalHistoryTable', '#chemicalHistoryPaginator']) {
      expect(find(id)).not.toBeNull();
    }
    expect(find('#chemicalHistoryPeriodError')).toBeNull();
    expect(rows()).toHaveLength(4);
  });

  it('shows the default period in the date fields', () => {
    render();

    expect((find('#chemicalHistoryFrom') as HTMLInputElement).value).toBe('2026-08-31');
    expect((find('#chemicalHistoryTo') as HTMLInputElement).value).toBe('2026-09-29');
  });

  it('reads a time without an offset as UTC, like one with "Z" or "+02:00" (E-16)', () => {
    render();

    expect(cell(1, 'time')).toMatch(/^\d{2}-09-2026 \d{2}:\d{2}$/);
    expect(cell(2, 'time')).toBe(cell(1, 'time'));
    expect(cell(3, 'time')).toBe(cell(1, 'time'));
  });

  it('names the system user, the action, the product, the location and the details', () => {
    render();

    expect(cell(0, 'user')).toBe('Chemicals: System');
    expect(cell(0, 'action')).toBe('Chemicals: Action StatusChanged');
    expect(cell(0, 'details')).toBe('Produkt afmeldt');
    expect(cell(1, 'user')).toBe('Mette Larsen');
    expect(cell(1, 'product')).toBe('Karate 2,5 WG');
    // The Location column is the event's location, as in the Excel; Details carries the move (m-9).
    expect(cell(1, 'location')).toBe('Lade');
    expect(cell(1, 'details')).toBe('Lade → Kemirum · 5 L');
    expect(cell(2, 'details')).toBe('Chemicals: History reason: Chemicals: Reason used · tom');
    expect(cell(3, 'product')).toBe('–');
    expect(cell(3, 'location')).toBe('–');
    expect(rows()[1].getAttribute('data-action')).toBe(String(ChemicalHistoryAction.Moved));
  });

  it('shows the reversed-period error instead of the table and blocks the export', () => {
    render();

    component.updateFilters({dateFrom: '2026-09-20', dateTo: '2026-09-10'});
    fixture.detectChanges();

    expect(find('#chemicalHistoryPeriodError')?.getAttribute('role')).toBe('alert');
    expect(find('#chemicalHistoryPeriodError')?.textContent).toContain('Chemicals: Period reversed');
    expect(find('#chemicalHistoryTable')).toBeNull();
    expect((find('#chemicalHistoryExportBtn') as HTMLButtonElement).disabled).toBe(true);
  });

  it('says it is loading, then that it failed, never an empty log', () => {
    render(NEVER);
    expect(find('#chemicalHistoryStatus')?.textContent).toContain('Chemicals: Loading history');
    expect(find('#chemicalHistoryEmpty')).toBeNull();
    fixture.destroy();
    TestBed.resetTestingModule();

    render(of({success: false}));
    expect(find('#chemicalHistoryLoadFailed')?.getAttribute('role')).toBe('alert');
    expect(find('#chemicalHistoryTable')).toBeNull();
    expect(find('#chemicalHistoryEmpty')).toBeNull();
  });

  it('says so when nothing happened in the period', () => {
    render(of({success: true, model: {events: [], total: 0, users: []}}));

    expect(find('#chemicalHistoryEmpty')?.textContent).toContain('Chemicals: No history');
  });

  it('disables the export while a file is on its way', () => {
    render();
    const button = find('#chemicalHistoryExportBtn') as HTMLButtonElement;

    button.click();
    fixture.detectChanges();

    expect(button.disabled).toBe(true);
    expect(button.getAttribute('aria-busy')).toBe('true');
  });

  it('commits a picked date on change', () => {
    render();
    const to = find('#chemicalHistoryTo') as HTMLInputElement;

    to.value = '2026-09-15';
    to.dispatchEvent(new Event('change'));

    expect(component.filters.dateTo).toBe('2026-09-15');
  });

  it('renders every time through asUtcIso, so it does not depend on the runner\'s zone (m-1)', () => {
    render();

    expect(utc).toHaveBeenCalledWith('2026-09-28T12:05:00');
    expect(utc).toHaveBeenCalledWith('2026-09-28T14:05:00+02:00');
    expect(utc.mock.results.map((r) => r.value)).toContain('2026-09-28T12:05:00Z');
    utc.mockRestore();
    expect(component.utc).toBe(asUtcIso);
  });

  it('hides the paginator when the log could not be loaded or the period is wrong (m-4)', () => {
    render(of({success: false}));
    expect(find('#chemicalHistoryPaginator')).toBeNull();
    fixture.destroy();
    TestBed.resetTestingModule();

    render();
    expect(find('#chemicalHistoryPaginator')).not.toBeNull();
    component.updateFilters({dateFrom: '2026-09-20', dateTo: '2026-09-10'});
    fixture.detectChanges();
    expect(find('#chemicalHistoryPaginator')).toBeNull();
  });

  it('names the table and keeps one live region whose text follows the load (m-7)', () => {
    render(NEVER);
    const status = find('#chemicalHistoryStatus');
    expect(status?.getAttribute('role')).toBe('status');
    expect(status?.textContent?.trim()).toBe('Chemicals: Loading history');
    fixture.destroy();
    TestBed.resetTestingModule();

    render();
    expect(find('#chemicalHistoryStatus')?.textContent?.trim()).toBe('');
    expect(find('#chemicalHistoryTable')?.getAttribute('aria-label')).toBe('Chemicals: History title');
  });

  it('says so under the location filter when the locations could not be loaded (m-8)', () => {
    render(undefined, undefined, of({success: false}));

    expect(find('#chemicalHistoryLocationsFailed')?.textContent).toContain('Chemicals: Could not load locations');
  });
});
