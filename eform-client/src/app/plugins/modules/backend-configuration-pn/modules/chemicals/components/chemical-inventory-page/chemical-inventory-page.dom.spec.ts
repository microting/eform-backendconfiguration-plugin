import {NO_ERRORS_SCHEMA} from '@angular/core';
import {ComponentFixture, TestBed} from '@angular/core/testing';
import {FormsModule} from '@angular/forms';
import {MatDialog} from '@angular/material/dialog';
import {MatMenuModule} from '@angular/material/menu';
import {NoopAnimationsModule} from '@angular/platform-browser/animations';
import {ActivatedRoute, convertToParamMap} from '@angular/router';
import {Store} from '@ngrx/store';
import {TranslateModule} from '@ngx-translate/core';
import {ToastrService} from 'ngx-toastr';
import {BehaviorSubject, of} from 'rxjs';
import {BackendConfigurationPnChemicalsService} from '../../../../services';
import {chemicalsInitialState, selectChemicalsInventoryFilters} from '../../../../state';
import {ChemicalInventoryPageComponent} from './chemical-inventory-page.component';

/**
 * Template-level checks for W1: the ids the e2e specs use, the "Kræver handling" banner and
 * the disabled states. mtx-grid is an unknown element under NO_ERRORS_SCHEMA, so rows are
 * covered by the component spec, not here.
 */
describe('ChemicalInventoryPageComponent (template)', () => {
  let fixture: ComponentFixture<ChemicalInventoryPageComponent>;
  let component: ChemicalInventoryPageComponent;
  let filters$: BehaviorSubject<typeof chemicalsInitialState.inventoryFilters>;
  const find = (selector: string): HTMLElement | null => fixture.nativeElement.querySelector(selector);
  const disabled = (selector: string) => (find(selector) as HTMLButtonElement).disabled;

  function render(locations: Array<{id: number; archived: boolean}>): void {
    filters$ = new BehaviorSubject({...chemicalsInitialState.inventoryFilters});
    const inventory = {
      properties: [{propertyId: 3, name: 'Hovedgården', permissions: null, stockEnabled: false, callerWorkerId: null}],
      locations: locations.map((l) => ({...l, propertyId: 3, name: `Lager ${l.id}`, description: '', photoFileName: null, sortOrder: l.id, updatedAt: ''})),
      placements: [{
        id: 7, locationId: 10, propertyId: 3, chemicalId: 200, productId: null, placementNote: '', registeredByUserId: 1,
        registeredByName: 'A', registeredAt: '2026-09-01T00:00:00', removedByUserId: null, removedByName: null, removedAt: null,
        removalReason: null, removalNote: null, movedFromPlacementId: null, balance: 0, unit: null, updatedAt: '', writeOffEntryId: null,
      }],
      stockEntries: [],
      // Afmeldt (6): requires action whatever today's date is.
      registerEntries: [{
        chemicalId: 200, name: 'Karate 2,5 WG', registrationNo: '1-212', status: 6, statusText: 'Produkt afmeldt',
        salesDeadline: null, useAndPossessionDeadline: null, authorisationDate: null, authorisationExpirationDate: null,
        authorisationTerminationDate: null, hazardPictograms: [], signalWord: null, signalWordText: '', hazardStatements: [],
        activeSubstances: [], authorisationHolder: '', products: [], updatedAt: '',
      }],
      syncToken: '',
      full: true,
    };
    TestBed.configureTestingModule({
      declarations: [ChemicalInventoryPageComponent],
      imports: [FormsModule, MatMenuModule, NoopAnimationsModule, TranslateModule.forRoot()],
      providers: [
        {provide: Store, useValue: {
          select: jest.fn().mockImplementation((selector) =>
            selector === selectChemicalsInventoryFilters ? filters$ : new BehaviorSubject<number | null>(3)),
          dispatch: jest.fn(),
        }},
        {provide: BackendConfigurationPnChemicalsService, useValue: {
          getPropertyInventory: jest.fn().mockReturnValue(of({success: true, model: inventory})),
        }},
        {provide: ActivatedRoute, useValue: {snapshot: {queryParamMap: convertToParamMap({})}}},
        {provide: MatDialog, useValue: {open: jest.fn()}},
        {provide: ToastrService, useValue: {error: jest.fn()}},
      ],
      schemas: [NO_ERRORS_SCHEMA],
    });
    fixture = TestBed.createComponent(ChemicalInventoryPageComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  afterEach(() => fixture?.destroy());

  it('renders every control the e2e page object addresses', () => {
    render([{id: 10, archived: false}]);

    for (const id of [
      'chemicalInventoryPage', 'chemicalsInventorySearch', 'chemicalsLocationFilter', 'chemicalsStatusFilter',
      'chemicalsRequiresActionChip', 'chemicalsExportBtn', 'chemicalsRegisterPlacementBtn',
    ]) {
      expect(find(`#${id}`)).not.toBeNull();
    }
  });

  it('shows the "Kræver handling" banner with a "show only these" action until the filter is on', () => {
    render([{id: 10, archived: false}]);

    expect(find('#chemicalsActionBanner')).not.toBeNull();
    expect(find('#chemicalsActionBanner')?.classList.contains('chemicals-banner--overdue')).toBe(true);
    expect(find('#chemicalsShowOnlyActionBtn')).not.toBeNull();

    filters$.next({...chemicalsInitialState.inventoryFilters, requiresActionOnly: true});
    fixture.detectChanges();

    expect(find('#chemicalsShowOnlyActionBtn')).toBeNull();
    expect(find('#chemicalsRequiresActionChip')?.classList.contains('chemicals-chip--active')).toBe(true);
  });

  it('disables the export button while a download is running', () => {
    render([{id: 10, archived: false}]);
    expect(disabled('#chemicalsExportBtn')).toBe(false);

    component.exporting = true;
    fixture.detectChanges();

    expect(disabled('#chemicalsExportBtn')).toBe(true);
  });

  it('offers "Registrér placering" only when the property has an active location', () => {
    render([{id: 10, archived: true}]);

    expect(disabled('#chemicalsRegisterPlacementBtn')).toBe(true);
  });
});
