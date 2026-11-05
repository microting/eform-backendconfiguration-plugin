import {describe, it, expect, afterEach, vi} from 'vitest';
import type {Mock} from 'vitest';
import {TestBed} from '@angular/core/testing';
import {FormBuilder} from '@angular/forms';
import {MatDialogRef} from '@angular/material/dialog';
import {Store} from '@ngrx/store';
import {NEVER, of, throwError} from 'rxjs';
import {TranslateService} from '@ngx-translate/core';
import {ToastrService} from 'ngx-toastr';
import {AuthStateService} from 'src/app/common/store';
import {AppSettingsStateService} from 'src/app/modules/application-settings/components/store';
import {PropertyCreateModel} from '../../../../models';
import {BackendConfigurationPnPropertiesService} from '../../../../services';
import {PropertyCreateModalComponent} from './property-create-modal.component';

/**
 * eform-angular-frontend#8141 — a new property's languages are stored as SDK Languages.Id.
 * Those are AUTO_INCREMENT values: a Galera cluster (auto_increment_increment=3) seeds
 * Dansk=3, English=6, Deutsch=9, so the app-locale ids 1/2/3 must be resolved by code.
 */
describe('PropertyCreateModalComponent — property languages (#8141)', () => {
  let toastr: {error: Mock};

  function create(languages$: any): {component: PropertyCreateModalComponent; emitted: PropertyCreateModel[]} {
    toastr = {error: vi.fn()};
    TestBed.configureTestingModule({
      providers: [
        FormBuilder,
        {provide: AuthStateService, useValue: {}},
        {provide: Store, useValue: {select: vi.fn().mockReturnValue(of(true))}},
        {provide: BackendConfigurationPnPropertiesService, useValue: {}},
        {provide: MatDialogRef, useValue: {close: vi.fn()}},
        {provide: ToastrService, useValue: toastr},
        {provide: TranslateService, useValue: {instant: (k: string) => k}},
        {provide: AppSettingsStateService, useValue: {getLanguages: vi.fn().mockReturnValue(languages$)}},
      ],
    });
    const component = TestBed.runInInjectionContext(() => new PropertyCreateModalComponent());
    component.ngOnInit();
    const emitted: PropertyCreateModel[] = [];
    component.propertyCreate.subscribe(p => emitted.push(p));
    component.newPropertyForm.patchValue({cvr: '1', name: 'Gården'});
    return {component, emitted};
  }

  afterEach(() => TestBed.resetTestingModule());

  it('sends the SDK ids of da / en-US / de-DE, resolved by language code', () => {
    const {component, emitted} = create(of({success: true, model: {languages: [
      {id: 3, languageCode: 'da', name: 'Dansk', isActive: true},
      {id: 6, languageCode: 'en-US', name: 'English', isActive: true},
      {id: 9, languageCode: 'de-DE', name: 'Deutsch', isActive: true},
      {id: 12, languageCode: 'pl-PL', name: 'Polski', isActive: false},
    ]}}));

    component.onCreateProperty();

    expect(emitted[0].languagesIds).toEqual([3, 6, 9]);
  });

  it('keeps Create disabled and emits nothing until the language list has loaded', () => {
    const {component, emitted} = create(NEVER);

    expect(component.isDisabledSaveButton).toBe(true);
    component.onCreateProperty();
    expect(emitted).toEqual([]);
  });

  it('shows an error and never sends positional ids when the language list fails to load', () => {
    const {component, emitted} = create(throwError(() => new Error('down')));

    expect(toastr.error).toHaveBeenCalledWith('Could not load languages');
    expect(component.isDisabledSaveButton).toBe(true);
    component.onCreateProperty();
    expect(emitted).toEqual([]);
  });

  it('treats an unsuccessful language response as a failure', () => {
    const {component, emitted} = create(of({success: false}));

    expect(toastr.error).toHaveBeenCalledWith('Could not load languages');
    component.onCreateProperty();
    expect(emitted).toEqual([]);
  });

  // #1477: the Halebid toggle sends tailBiteEnabled with the new property; off unless switched on.
  it('sends tailBiteEnabled: false by default and true once the Halebid toggle is on', () => {
    const languages = of({success: true, model: {languages: [{id: 3, languageCode: 'da', name: 'Dansk', isActive: true}]}});
    const {component, emitted} = create(languages);

    component.onCreateProperty();
    component.newPropertyForm.patchValue({tailBiteEnabled: true});
    component.onCreateProperty();

    expect(emitted.map(p => p.tailBiteEnabled)).toEqual([false, true]);
  });
});
