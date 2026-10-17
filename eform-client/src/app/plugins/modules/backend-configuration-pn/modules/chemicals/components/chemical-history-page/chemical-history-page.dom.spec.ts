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

  function render(answer?: Observable<unknown>, exportAnswer?: Observable<unknown>): void {
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
          getPropertyInventory: jest.fn().mockReturnValue(of({success: true, model: {locations: [location(10, 'Lade', {propertyId: 3})]}})),
          exportHistory: jest.fn().mockReturnValue(exportAnswer ?? NEVER),
        }},
        {provide: ToastrService, useValue: {error: jest.fn()}},
      ],
      schemas: [NO_ERRORS_SCHEMA],
    });
    fixture = TestBed.createComponent(ChemicalHistoryPageComponent);
    component = fixture.componentInstance;
    component.today = '2026-09-29';
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
    expect(cell(1, 'location')).toBe('Lade → Kemirum');
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
    expect(find('#chemicalHistoryLoading')?.getAttribute('role')).toBe('status');
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
});
