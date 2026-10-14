import {NO_ERRORS_SCHEMA} from '@angular/core';
import {ComponentFixture, TestBed} from '@angular/core/testing';
import {FormsModule} from '@angular/forms';
import {MatDialog} from '@angular/material/dialog';
import {MatMenuModule} from '@angular/material/menu';
import {NoopAnimationsModule} from '@angular/platform-browser/animations';
import {Store} from '@ngrx/store';
import {TranslateModule} from '@ngx-translate/core';
import {ToastrService} from 'ngx-toastr';
import {BehaviorSubject, of} from 'rxjs';
import {BackendConfigurationPnChemicalsService, BackendConfigurationPnPropertiesService} from '../../../../services';
import {NO_CHEMICAL_FLAGS} from '../../helpers';
import {ChemicalPermissionsPageComponent} from './chemical-permissions-page.component';

/**
 * Template-level checks for W4. Material checkboxes and form fields are unknown
 * elements under NO_ERRORS_SCHEMA, so the assertions use the ids and classes the
 * e2e specs and the styles rely on.
 */
describe('ChemicalPermissionsPageComponent (template)', () => {
  const flags = (partial: Partial<typeof NO_CHEMICAL_FLAGS>) => ({...NO_CHEMICAL_FLAGS, ...partial});
  let fixture: ComponentFixture<ChemicalPermissionsPageComponent>;
  let component: ChemicalPermissionsPageComponent;
  const find = (selector: string): HTMLElement | null => fixture.nativeElement.querySelector(selector);

  function render(properties: Array<{id: number; name: string}>): void {
    TestBed.configureTestingModule({
      declarations: [ChemicalPermissionsPageComponent],
      imports: [FormsModule, MatMenuModule, NoopAnimationsModule, TranslateModule.forRoot()],
      providers: [
        {provide: Store, useValue: {select: jest.fn().mockReturnValue(new BehaviorSubject<number | null>(3))}},
        {provide: BackendConfigurationPnChemicalsService, useValue: {
          getPermissions: jest.fn().mockReturnValue(of({success: true, model: [
            {workerId: 1, workerName: 'Jens Hansen', flags: flags({view: true})},
            {workerId: 2, workerName: 'Ole Nielsen', flags: flags({})},
          ]})),
          savePermissions: jest.fn(),
        }},
        {provide: BackendConfigurationPnPropertiesService, useValue: {
          getAllPropertiesDictionary: jest.fn().mockReturnValue(of({success: true, model: properties})),
        }},
        {provide: MatDialog, useValue: {open: jest.fn()}},
        {provide: ToastrService, useValue: {success: jest.fn(), info: jest.fn(), error: jest.fn()}},
      ],
      schemas: [NO_ERRORS_SCHEMA],
    });
    fixture = TestBed.createComponent(ChemicalPermissionsPageComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  afterEach(() => fixture?.destroy());

  it('keeps the row element when a flag is toggled, so keyboard focus survives (I-2)', () => {
    render([{id: 3, name: 'Hovedgården'}]);
    const before = find('#chemicalPermissionRow-1');
    expect(before).not.toBeNull();

    component.toggle(component.rows[0], 'stock');
    fixture.detectChanges();

    expect(find('#chemicalPermissionRow-1')).toBe(before);
  });

  it('draws implied flags with the dashed "Følger af Admin" style (m-1)', () => {
    render([{id: 3, name: 'Hovedgården'}]);
    component.toggle(component.rows[0], 'admin');
    fixture.detectChanges();

    expect(find('#chemicalPermission-stock-1')?.classList.contains('flag-checkbox--implied')).toBe(true);
    expect(find('#chemicalPermission-admin-1')?.classList.contains('flag-checkbox--implied')).toBe(false);
  });

  it('shows a "no match" row when the search finds nobody (m-7)', () => {
    render([{id: 3, name: 'Hovedgården'}]);
    expect(find('#chemicalPermissionsNoMatch')).toBeNull();

    component.search = 'zzz';
    fixture.detectChanges();

    expect(find('#chemicalPermissionsNoMatch')).not.toBeNull();
    expect(find('#chemicalPermissionRow-1')).toBeNull();
  });

  it('shows the property scope note only when the property name is known (m-5)', () => {
    render([{id: 3, name: 'Hovedgården'}]);
    expect(find('#chemicalPermissionsScopeNote')).not.toBeNull();
  });

  it('leaves the scope note out rather than reading "gælder kun for ." (m-5)', () => {
    render([]);
    expect(find('#chemicalPermissionsScopeNote')).toBeNull();
    expect(find('#chemicalPermissionsPushNote')).not.toBeNull();
  });
});
