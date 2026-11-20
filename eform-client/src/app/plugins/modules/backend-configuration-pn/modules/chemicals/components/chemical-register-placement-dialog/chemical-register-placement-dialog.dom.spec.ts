import {describe, it, expect, afterEach, vi} from 'vitest';
import {NO_ERRORS_SCHEMA} from '@angular/core';
import {ComponentFixture, TestBed} from '@angular/core/testing';
import {FormsModule} from '@angular/forms';
import {MAT_DIALOG_DATA, MatDialogRef} from '@angular/material/dialog';
import {MatRadioModule} from '@angular/material/radio';
import {NoopAnimationsModule} from '@angular/platform-browser/animations';
import {MtxSelectModule} from '@ng-matero/extensions/select';
import {TranslateModule} from '@ngx-translate/core';
import {of} from 'rxjs';
import {BackendConfigurationPnChemicalsService} from '../../../../services';
import {ChemicalRegisterPlacementDialogComponent} from './chemical-register-placement-dialog.component';
import {detectChanges} from '../../../../testing/detect-changes';

/** Template-level checks for "Registrér placering": ids and accessible names (m-6, m-7), the "more matches" note (m-4). */
describe('ChemicalRegisterPlacementDialogComponent (template)', () => {
  let fixture: ComponentFixture<ChemicalRegisterPlacementDialogComponent>;
  let component: ChemicalRegisterPlacementDialogComponent;
  const find = (selector: string): HTMLElement | null => fixture.nativeElement.querySelector(selector);

  function render(stockEnabled: boolean, data?: Record<string, unknown>): void {
    TestBed.configureTestingModule({
      // Real radio groups and mtx-selects: ngModel needs their value accessors.
      imports: [ChemicalRegisterPlacementDialogComponent, FormsModule, MatRadioModule, MtxSelectModule, NoopAnimationsModule, TranslateModule.forRoot()],
      providers: [
        {provide: BackendConfigurationPnChemicalsService, useValue: {
          searchRegister: vi.fn().mockReturnValue(of({success: true, model: {total: 40, entries: [{
            chemicalId: 200, name: 'Karate 2,5 WG', registrationNo: '1-212', products: [
              {productId: 2000, name: 'Karate 1 L', barcode: ''},
              {productId: 2001, name: 'Karate 5 L', barcode: ''},
            ],
          }]}})),
        }},
        {provide: MatDialogRef, useValue: {close: vi.fn()}},
        {provide: MAT_DIALOG_DATA, useValue: data ?? {propertyId: 3, stockEnabled, locations: [{id: 10, name: 'Lade', archived: false}]}},
      ],
      schemas: [NO_ERRORS_SCHEMA],
    });
    fixture = TestBed.createComponent(ChemicalRegisterPlacementDialogComponent);
    component = fixture.componentInstance;
    detectChanges(fixture);
  }

  afterEach(() => fixture?.destroy());

  it('names the icon-only search button and the unit choice', () => {
    render(true);

    expect(find('#chemicalRegisterSearchBtn')?.getAttribute('aria-label')).toBe('Search');
    expect(find('#chemicalRegisterUnit')?.getAttribute('aria-label')).toBe('Chemicals: Unit');
  });

  it('gives the package select an id and notes that the register has more matches', () => {
    render(false);
    component.query = 'karate';
    component.search();
    component.select(component.results[0]);
    detectChanges(fixture);

    expect(find('#chemicalRegisterProductSelect')).not.toBeNull();
    expect(find('#chemicalRegisterMoreResults')).not.toBeNull();
  });
  it('shows the Ejendom field first: read-only for the selected property, a choice under "All"', async () => {
    // The value and [disabled] reach the shown label in two hops, each a microtask after a change detection:
    // the outer NgModel writes them into mtx-select, whose template hands them on through its own
    // [(ngModel)]/[disabled] to the inner ng-select, which then renders the selected label. One pass shows
    // the disabled class but not yet the label, so settle like the container's picker spec does.
    const settle = async () => {
      for (let pass = 0; pass < 3; pass++) {
        detectChanges(fixture);
        await fixture.whenStable();
        await new Promise((resolve) => setTimeout(resolve));
      }
    };
    const properties = [
      {propertyId: 3, name: 'Engvej 12', permissions: null, stockEnabled: false, callerWorkerId: null},
      {propertyId: 4, name: 'Østergård', permissions: null, stockEnabled: false, callerWorkerId: null},
    ];
    const locations = [{id: 10, propertyId: 3, name: 'Lade', archived: false}, {id: 20, propertyId: 4, name: 'Værksted', archived: false}];
    render(false, {propertyId: 3, stockEnabled: false, properties: [properties[0]], locations: [locations[0]]});
    await settle();

    const field = find('#chemicalRegisterPropertySelect');
    expect(field).not.toBeNull();
    expect(field?.classList.contains('mtx-select-disabled')).toBe(true);
    expect(field?.textContent).toContain('Engvej 12');
    fixture.destroy();
    TestBed.resetTestingModule();

    render(false, {propertyId: null, stockEnabled: false, properties, locations});
    await settle();

    expect(find('#chemicalRegisterPropertySelect')?.classList.contains('mtx-select-disabled')).toBe(false);
    const fields = Array.from(fixture.nativeElement.querySelectorAll('mtx-select')).map((el) => (el as HTMLElement).id);
    expect(fields.indexOf('chemicalRegisterPropertySelect')).toBeLessThan(fields.indexOf('chemicalRegisterLocationSelect'));
  });

  it('has no Ejendom field without a property list', () => {
    render(false);

    expect(find('#chemicalRegisterPropertySelect')).toBeNull();
  });
});
