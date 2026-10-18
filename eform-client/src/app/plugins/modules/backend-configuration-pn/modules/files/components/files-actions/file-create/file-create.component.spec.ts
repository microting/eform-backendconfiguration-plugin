import {describe, it, expect, afterEach, vi} from 'vitest';
// ng2-pdf-viewer assigns to the pdfjs-dist module namespace when it loads, which
// throws under the unit-test builder (ESM namespaces are not extensible), and
// pdfjs-dist cannot be mocked from inside the package. No spec renders a PDF.
vi.mock('ng2-pdf-viewer', () => ({PdfViewerModule: class {}, PdfViewerComponent: class {}}));
import {CommonModule} from '@angular/common';
import {NO_ERRORS_SCHEMA} from '@angular/core';
import {detectChanges} from '../../../../../testing/detect-changes';
import {ComponentFixture, TestBed} from '@angular/core/testing';
import {ActivatedRoute, Router} from '@angular/router';
import {Store} from '@ngrx/store';
import {TranslateModule} from '@ngx-translate/core';
import {of} from 'rxjs';
import {
  BackendConfigurationPnArchiveFoldersService,
  BackendConfigurationPnFilesService,
  BackendConfigurationPnFileTagsService,
  BackendConfigurationPnPropertiesService,
} from '../../../../../services';
import {FileCreateComponent} from './file-create.component';

const folder = (id: number, isDefault = false) => ({
  id, name: `Folder ${id}`, parentId: null, isDefault, count: 0, readGroups: [], ownGroups: false, restricted: false,
});

describe('FileCreateComponent folder preselection', () => {
  let fixture: ComponentFixture<FileCreateComponent>;
  let component: FileCreateComponent;

  function setup(requested: string | null, storeFolderId: number | null) {
    TestBed.configureTestingModule({
      imports: [FileCreateComponent, TranslateModule.forRoot()],
      schemas: [NO_ERRORS_SCHEMA],
      providers: [
        {provide: Store, useValue: {select: () => of(storeFolderId)}},
        {provide: Router, useValue: {navigate: vi.fn()}},
        {provide: ActivatedRoute, useValue: {snapshot: {queryParamMap: {get: () => requested}}}},
        {
          provide: BackendConfigurationPnArchiveFoldersService,
          useValue: {getFolders: () => of({success: true, message: '', model: [folder(3), folder(7, true), folder(9)]})},
        },
        {provide: BackendConfigurationPnPropertiesService, useValue: {getAllPropertiesDictionary: () => of({success: true, model: []})}},
        {provide: BackendConfigurationPnFileTagsService, useValue: {getTags: () => of({success: true, model: []})}},
        {provide: BackendConfigurationPnFilesService, useValue: {createFiles: vi.fn()}},
      ],
    });
    // Keep the scope these specs were written for (the old TestBed module had only
    // CommonModule + these imports): the component's real Material/forms imports
    // would make them test Material instead of this component.
    TestBed.overrideComponent(FileCreateComponent, {set: {imports: [CommonModule, TranslateModule], schemas: [NO_ERRORS_SCHEMA]}});
    fixture = TestBed.createComponent(FileCreateComponent);
    component = fixture.componentInstance;
    detectChanges(fixture);
  }

  afterEach(() => fixture?.destroy());

  it('a known ?folderId= wins over the store folder and the default', () => {
    setup('9', 3);
    expect(component.selectedFolderId).toBe(9);
  });

  it('an unknown ?folderId= falls back to the store folder', () => {
    setup('42', 3);
    expect(component.selectedFolderId).toBe(3);
  });

  it('falls back to the default folder when neither is known', () => {
    setup(null, 55);
    expect(component.selectedFolderId).toBe(7);
  });

  it('requires a folder: upload disabled and hint shown until one is chosen', () => {
    setup(null, null);
    component.files = [{file: new File(['x'], 'a.pdf'), tagIds: [], propertyIds: [1]}];
    component.selectedProperties = [1];
    component.selectedFolderId = null;
    detectChanges(fixture);
    expect(component.disabledUploadBtn).toBe(true);
    expect(fixture.nativeElement.querySelector('#archiveUploadFolderRequired')).not.toBeNull();

    component.selectedFolderId = 3;
    detectChanges(fixture);
    expect(component.disabledUploadBtn).toBe(false);
    expect(fixture.nativeElement.querySelector('#archiveUploadFolderRequired')).toBeNull();
  });
});
