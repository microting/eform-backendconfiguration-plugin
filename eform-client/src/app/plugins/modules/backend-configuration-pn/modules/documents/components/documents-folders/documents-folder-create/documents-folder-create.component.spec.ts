import {TestBed} from '@angular/core/testing';
import {of} from 'rxjs';
import {MatDialogRef} from '@angular/material/dialog';
import {Store} from '@ngrx/store';
import {ToastrService} from 'ngx-toastr';
import {TranslateService} from '@ngx-translate/core';
import {LocaleService} from 'src/app/common/services';
import {applicationLanguages2} from 'src/app/common/const';
import {BackendConfigurationPnDocumentsService} from '../../../../../services';
import {DocumentsFolderCreateComponent} from './documents-folder-create.component';

describe('DocumentsFolderCreateComponent', () => {
  let component: DocumentsFolderCreateComponent;
  let dialogRefSpy: any;
  let documentsServiceSpy: any;
  let toastrSpy: any;

  beforeEach(() => {
    dialogRefSpy = {close: jest.fn()};
    documentsServiceSpy = {createFolder: jest.fn()};
    toastrSpy = {error: jest.fn()};
    TestBed.configureTestingModule({
      providers: [
        {provide: Store, useValue: {select: jest.fn().mockReturnValue(of(1))}},
        {provide: BackendConfigurationPnDocumentsService, useValue: documentsServiceSpy},
        {provide: ToastrService, useValue: toastrSpy},
        {provide: TranslateService, useValue: {instant: (key: string) => key}},
        {provide: LocaleService, useValue: {}},
        {provide: MatDialogRef, useValue: dialogRefSpy},
      ],
    });
    component = TestBed.runInInjectionContext(() => new DocumentsFolderCreateComponent());
    component.ngOnInit();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('initCreateForm seeds one empty translation per application language', () => {
    expect(component.newFolderModel.translations.length).toBe(applicationLanguages2.length);
    expect(component.newFolderModel.translations.every((t) => t.name === '' && t.description === '')).toBe(true);
  });

  it('createFolder without any named translation toasts an error and does not call the service', () => {
    component.createFolder();
    expect(toastrSpy.error).toHaveBeenCalled();
    expect(documentsServiceSpy.createFolder).not.toHaveBeenCalled();
  });

  it('hide() closes the dialog and clears the name', () => {
    component.name = 'Mappe';
    component.hide();
    expect(dialogRefSpy.close).toHaveBeenCalled();
    expect(component.name).toBe('');
  });
});
