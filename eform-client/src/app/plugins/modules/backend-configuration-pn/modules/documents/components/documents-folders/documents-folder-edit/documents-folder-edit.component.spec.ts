import {TestBed} from '@angular/core/testing';
import {of} from 'rxjs';
import {MAT_DIALOG_DATA, MatDialogRef} from '@angular/material/dialog';
import {Store} from '@ngrx/store';
import {ToastrService} from 'ngx-toastr';
import {TranslateService} from '@ngx-translate/core';
import {LocaleService} from 'src/app/common/services';
import {BackendConfigurationPnDocumentsService} from '../../../../../services';
import {DocumentsFolderEditComponent} from './documents-folder-edit.component';

describe('DocumentsFolderEditComponent', () => {
  let component: DocumentsFolderEditComponent;
  let dialogRefSpy: any;
  let documentsServiceSpy: any;
  let toastrSpy: any;

  beforeEach(() => {
    dialogRefSpy = {close: jest.fn()};
    documentsServiceSpy = {
      getSingleFolder: jest.fn().mockReturnValue(of({success: false})),
      updateFolder: jest.fn(),
    };
    toastrSpy = {error: jest.fn()};
    TestBed.configureTestingModule({
      providers: [
        {provide: Store, useValue: {select: jest.fn().mockReturnValue(of(1))}},
        {provide: BackendConfigurationPnDocumentsService, useValue: documentsServiceSpy},
        {provide: ToastrService, useValue: toastrSpy},
        {provide: TranslateService, useValue: {instant: (key: string) => key}},
        {provide: LocaleService, useValue: {}},
        {provide: MatDialogRef, useValue: dialogRefSpy},
        {provide: MAT_DIALOG_DATA, useValue: {id: 5, documentFolderTranslations: []}},
      ],
    });
    component = TestBed.runInInjectionContext(() => new DocumentsFolderEditComponent());
    component.ngOnInit();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('ngOnInit fetches the folder passed via MAT_DIALOG_DATA', () => {
    expect(documentsServiceSpy.getSingleFolder).toHaveBeenCalledWith(5);
  });

  it('updateFolder without any named translation toasts an error and does not call the service', () => {
    component.folderUpdateModel = {id: 5, documentFolderTranslations: [], isDeletable: true};
    component.updateFolder();
    expect(toastrSpy.error).toHaveBeenCalled();
    expect(documentsServiceSpy.updateFolder).not.toHaveBeenCalled();
  });

  it('hide() closes the dialog', () => {
    component.hide();
    expect(dialogRefSpy.close).toHaveBeenCalled();
  });
});
