import {describe, it, expect, afterEach, vi} from 'vitest';
import {NO_ERRORS_SCHEMA} from '@angular/core';
import {ComponentFixture, TestBed} from '@angular/core/testing';
import {FormsModule} from '@angular/forms';
import {MatDialog} from '@angular/material/dialog';
import {MatMenuModule} from '@angular/material/menu';
import {NoopAnimationsModule} from '@angular/platform-browser/animations';
import {ActivatedRoute, Router, convertToParamMap} from '@angular/router';
import {MtxGridModule} from '@ng-matero/extensions/grid';
import {MtxSelectModule} from '@ng-matero/extensions/select';
import {Store} from '@ngrx/store';
import {TranslateModule} from '@ngx-translate/core';
import {ToastrService} from 'ngx-toastr';
import {BehaviorSubject, Observable, Subject, of, throwError} from 'rxjs';
import {BackendConfigurationPnChemicalsService} from '../../../../services';
import {chemicalsInitialState, selectChemicalsInventoryFilters} from '../../../../state';
import {entry, inventoryOf, location, placement} from '../../helpers/chemical-inventory.fixtures';
import {ChemicalInventoryPageComponent} from './chemical-inventory-page.component';
import {detectChanges} from '../../../../testing/detect-changes';

/**
 * Template-level checks for W1: the ids the e2e specs use, the "Kræver handling" banner,
 * the loading and failed states, the disabled states and the accessible names. The real
 * mtx-grid renders the rows.
 */
describe('ChemicalInventoryPageComponent (template)', () => {
  let fixture: ComponentFixture<ChemicalInventoryPageComponent>;
  let component: ChemicalInventoryPageComponent;
  let filters$: BehaviorSubject<typeof chemicalsInitialState.inventoryFilters>;
  const find = (selector: string): HTMLElement | null => fixture.nativeElement.querySelector(selector);
  const disabled = (selector: string) => (find(selector) as HTMLButtonElement).disabled;

  function render(locations: Array<{id: number; archived: boolean}>, answer?: Observable<unknown>, stockEnabled = false): void {
    filters$ = new BehaviorSubject({...chemicalsInitialState.inventoryFilters});
    const inventory = inventoryOf({
      propertyId: 3,
      stockEnabled,
      locations: locations.map((l) => location(l.id, `Lager ${l.id}`, {propertyId: 3, archived: l.archived})),
      placements: [placement(7, 10, 200, {propertyId: 3})],
      // Afmeldt (6): requires action whatever today's date is.
      registerEntries: [entry(200, 'Karate 2,5 WG', {registrationNo: '1-212', status: 6, statusText: 'Produkt afmeldt', products: []})],
    });
    TestBed.configureTestingModule({
      // The real mtx-select: ngModel needs its value accessor.
      imports: [ChemicalInventoryPageComponent, FormsModule, MatMenuModule, MtxGridModule, MtxSelectModule, NoopAnimationsModule, TranslateModule.forRoot()],
      providers: [
        {provide: Store, useValue: {
          select: vi.fn().mockImplementation((selector) =>
            selector === selectChemicalsInventoryFilters ? filters$ : new BehaviorSubject<number | null>(3)),
          dispatch: vi.fn(),
        }},
        {provide: BackendConfigurationPnChemicalsService, useValue: {
          getPropertyInventory: vi.fn().mockReturnValue(answer ?? of({success: true, model: inventory})),
        }},
        {provide: ActivatedRoute, useValue: {snapshot: {queryParamMap: convertToParamMap({})}}},
        {provide: MatDialog, useValue: {open: vi.fn()}},
        {provide: ToastrService, useValue: {error: vi.fn()}},
        {provide: Router, useValue: {navigate: vi.fn(), currentNavigation: vi.fn().mockReturnValue(null)}},
      ],
      schemas: [NO_ERRORS_SCHEMA],
    });
    fixture = TestBed.createComponent(ChemicalInventoryPageComponent);
    component = fixture.componentInstance;
    detectChanges(fixture);
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
    detectChanges(fixture);

    expect(find('#chemicalsShowOnlyActionBtn')).toBeNull();
    expect(find('#chemicalsRequiresActionChip')?.classList.contains('chemicals-chip--active')).toBe(true);
  });

  it('disables the export button while a download is running', () => {
    render([{id: 10, archived: false}]);
    expect(disabled('#chemicalsExportBtn')).toBe(false);

    component.exporting = true;
    detectChanges(fixture);

    expect(disabled('#chemicalsExportBtn')).toBe(true);
  });

  it('offers "Registrér placering" only when the property has an active location', () => {
    render([{id: 10, archived: true}]);

    expect(disabled('#chemicalsRegisterPlacementBtn')).toBe(true);
  });

  it('shows neither "no placements" nor a 0 chip while the inventory loads, and keeps chip and export off (I-1)', () => {
    render([{id: 10, archived: false}], new Subject());

    expect(find('#chemicalInventoryLoading')).not.toBeNull();
    expect(fixture.nativeElement.textContent).not.toContain('Chemicals: No placements');
    expect(find('#chemicalsRequiresActionChip')?.textContent).not.toMatch(/\d/);
    expect(disabled('#chemicalsRequiresActionChip')).toBe(true);
    expect(disabled('#chemicalsExportBtn')).toBe(true);
    expect(find('#chemicalsActionBanner')).toBeNull();
  });

  it('shows an alert instead of an empty table when the inventory cannot be loaded (I-1)', () => {
    render([{id: 10, archived: false}], throwError(() => new Error('500')));

    expect(find('#chemicalInventoryLoadFailed')?.getAttribute('role')).toBe('alert');
    expect(find('mtx-grid')).toBeNull();
    expect(disabled('#chemicalsRequiresActionChip')).toBe(true);
    expect(disabled('#chemicalsExportBtn')).toBe(true);
  });

  it('names its controls for assistive technology (m-6, m-7)', () => {
    render([{id: 10, archived: false}]);

    expect(find('#chemicalsRequiresActionChip')?.getAttribute('aria-pressed')).toBe('false');
    expect(find('#chemicalsActionBanner')?.getAttribute('role')).toBe('status');
    expect(find('#action-items-0 button')?.getAttribute('aria-label')).toBe('Actions');
    const product = find('#chemicalRowProduct-0');
    expect(product?.tagName).toBe('BUTTON');
    expect(product?.textContent).toContain('Karate 2,5 WG');
  });

  describe('row menu (W2 placement actions)', () => {
    const openRowMenu = () => {
      (find('#action-items-0 button') as HTMLButtonElement).click();
      detectChanges(fixture);
    };
    // The menu panel renders in the CDK overlay container, outside the fixture.
    const menuItem = (id: string) => document.querySelector(`#${id}`);

    it('offers move, note and remove, and no stock movement while stock is off', () => {
      render([{id: 10, archived: false}]);

      openRowMenu();

      expect(menuItem('chemicalRowMove-0')).not.toBeNull();
      expect(menuItem('chemicalRowNote-0')).not.toBeNull();
      expect(menuItem('chemicalRowRemove-0')).not.toBeNull();
      expect(menuItem('chemicalRowStock-0')).toBeNull();
    });

    it('offers the stock movement when stock is on', () => {
      render([{id: 10, archived: false}], undefined, true);

      openRowMenu();

      expect(menuItem('chemicalRowStock-0')).not.toBeNull();
    });
  });
});
