import {describe, it, expect, afterEach, vi} from 'vitest';
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
import {BackendConfigurationPnChemicalsService} from '../../../../services';
import {selectChemicalsProperties} from '../../../../state';
import {NO_CHEMICAL_FLAGS} from '../../helpers';
import {ChemicalPermissionsPageComponent} from './chemical-permissions-page.component';
import {detectChanges} from '../../../../testing/detect-changes';
import {CommonModule} from '@angular/common';

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
      imports: [ChemicalPermissionsPageComponent, FormsModule, MatMenuModule, NoopAnimationsModule, TranslateModule.forRoot()],
      providers: [
        {provide: Store, useValue: {select: vi.fn().mockImplementation((selector) =>
          selector === selectChemicalsProperties
            ? of(properties.map((p) => ({...p, description: ''})))
            : new BehaviorSubject<number | null>(3))}},
        {provide: BackendConfigurationPnChemicalsService, useValue: {
          getPermissions: vi.fn().mockReturnValue(of({success: true, model: [
            {workerId: 1, workerName: 'Jens Hansen', flags: flags({view: true})},
            {workerId: 2, workerName: 'Ole Nielsen', flags: flags({})},
          ]})),
          savePermissions: vi.fn(),
        }},
        {provide: MatDialog, useValue: {open: vi.fn()}},
        {provide: ToastrService, useValue: {success: vi.fn(), info: vi.fn(), error: vi.fn()}},
      ],
      schemas: [NO_ERRORS_SCHEMA],
    })
      // Keep the scope these specs were written for (the old TestBed module), not the
      // component's own Material imports.
      .overrideComponent(ChemicalPermissionsPageComponent, {set: {imports: [CommonModule, FormsModule, MatMenuModule, TranslateModule], schemas: [NO_ERRORS_SCHEMA]}});
    fixture = TestBed.createComponent(ChemicalPermissionsPageComponent);
    component = fixture.componentInstance;
    detectChanges(fixture);
  }

  afterEach(() => fixture?.destroy());

  it('separates the two footnote sentences with a space (855 B3: "ejendom.Push-besked")', () => {
    render([{id: 3, name: 'Hovedgården'}]);

    expect(find('.chemicals-footnote')?.textContent?.replace(/\s+/g, ' ').trim())
      .toBe('Chemicals: Permissions scope note Chemicals: Push recipients note');
  });

  it('keeps the row element when a flag is toggled, so keyboard focus survives (I-2)', () => {
    render([{id: 3, name: 'Hovedgården'}]);
    const before = find('#chemicalPermissionRow-1');
    expect(before).not.toBeNull();

    component.toggle(component.rows[0], 'stock');
    detectChanges(fixture);

    expect(find('#chemicalPermissionRow-1')).toBe(before);
  });

  it('locks the matrix, Save and Undo while a copy is in flight (Copilot C-2)', () => {
    render([{id: 3, name: 'Hovedgården'}]);
    component.toggle(component.rows[0], 'stock');
    detectChanges(fixture);
    const disabled = (selector: string) => (find(selector) as unknown as {disabled: boolean}).disabled;
    expect(disabled('#chemicalPermission-view-1')).toBe(false);
    expect(disabled('#chemicalPermissionsSaveBtn')).toBe(false);
    expect(disabled('#chemicalPermissionsUndoBtn')).toBe(false);

    component.copying = true;
    detectChanges(fixture);

    expect(disabled('#chemicalPermission-view-1')).toBe(true);
    expect(disabled('#chemicalPermission-admin-1')).toBe(true);
    expect(disabled('#chemicalPermissionsSaveBtn')).toBe(true);
    expect(disabled('#chemicalPermissionsUndoBtn')).toBe(true);
  });

  it('draws implied flags with the dashed "Følger af Admin" style (m-1)', () => {
    render([{id: 3, name: 'Hovedgården'}]);
    component.toggle(component.rows[0], 'admin');
    detectChanges(fixture);

    expect(find('#chemicalPermission-stock-1')?.classList.contains('flag-checkbox--implied')).toBe(true);
    expect(find('#chemicalPermission-admin-1')?.classList.contains('flag-checkbox--implied')).toBe(false);
  });

  it('shows a "no match" row when the search finds nobody (m-7)', () => {
    render([{id: 3, name: 'Hovedgården'}]);
    expect(find('#chemicalPermissionsNoMatch')).toBeNull();

    component.search = 'zzz';
    detectChanges(fixture);

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
