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

/** Template-level checks for "Registrér placering": ids and accessible names (m-6, m-7), the "more matches" note (m-4). */
describe('ChemicalRegisterPlacementDialogComponent (template)', () => {
  let fixture: ComponentFixture<ChemicalRegisterPlacementDialogComponent>;
  let component: ChemicalRegisterPlacementDialogComponent;
  const find = (selector: string): HTMLElement | null => fixture.nativeElement.querySelector(selector);

  function render(stockEnabled: boolean): void {
    TestBed.configureTestingModule({
      declarations: [ChemicalRegisterPlacementDialogComponent],
      // Real radio groups and mtx-selects: ngModel needs their value accessors.
      imports: [FormsModule, MatRadioModule, MtxSelectModule, NoopAnimationsModule, TranslateModule.forRoot()],
      providers: [
        {provide: BackendConfigurationPnChemicalsService, useValue: {
          searchRegister: jest.fn().mockReturnValue(of({success: true, model: {total: 40, entries: [{
            chemicalId: 200, name: 'Karate 2,5 WG', registrationNo: '1-212', products: [
              {productId: 2000, name: 'Karate 1 L', barcode: ''},
              {productId: 2001, name: 'Karate 5 L', barcode: ''},
            ],
          }]}})),
        }},
        {provide: MatDialogRef, useValue: {close: jest.fn()}},
        {provide: MAT_DIALOG_DATA, useValue: {propertyId: 3, stockEnabled, locations: [{id: 10, name: 'Lade', archived: false}]}},
      ],
      schemas: [NO_ERRORS_SCHEMA],
    });
    fixture = TestBed.createComponent(ChemicalRegisterPlacementDialogComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
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
    fixture.detectChanges();

    expect(find('#chemicalRegisterProductSelect')).not.toBeNull();
    expect(find('#chemicalRegisterMoreResults')).not.toBeNull();
  });
});
