import {describe, it, expect, afterEach, vi} from 'vitest';
import type {Mock} from 'vitest';
// ng2-pdf-viewer assigns to the pdfjs-dist module namespace when it loads, which
// throws under the unit-test builder (ESM namespaces are not extensible), and
// pdfjs-dist cannot be mocked from inside the package. No spec renders a PDF.
vi.mock('ng2-pdf-viewer', () => ({PdfViewerModule: class {}, PdfViewerComponent: class {}}));
import {CommonModule} from '@angular/common';
import {NO_ERRORS_SCHEMA} from '@angular/core';
import {detectChanges} from '../../../../testing/detect-changes';
import {ComponentFixture, TestBed} from '@angular/core/testing';
import {MAT_DIALOG_DATA, MatDialogRef} from '@angular/material/dialog';
import {TranslateModule} from '@ngx-translate/core';
import {EMPTY, of} from 'rxjs';
import {
  BackendConfigurationPnArchiveFoldersService,
  BackendConfigurationPnFileTagsService,
  BackendConfigurationPnInboxService,
  BackendConfigurationPnPropertiesService,
} from '../../../../services';
import {
  ArchiveFolderModel,
  InboxDocumentStatus,
  InboxListItemModel,
  InboxSuggestionKind,
  FileInboxDocumentModel,
  InboxSuggestionModel,
} from '../../../../models';
import {InboxReviewDialogComponent} from './inbox-review-dialog.component';

const folder = (id: number, isDefault = false): ArchiveFolderModel => ({
  id, name: `Folder ${id}`, parentId: null, isDefault, count: 0, readGroups: [], ownGroups: false, restricted: false,
});

const suggestion = (kind: InboxSuggestionKind, targetId: number, confidence: number, extra: Partial<InboxSuggestionModel> = {}):
  InboxSuggestionModel => ({
  id: targetId * 10 + kind, kind, targetId, targetName: `Target ${targetId}`, source: 0, confidence,
  evidence: null, page: null, reason: null, targetDate: null, ...extra,
});

const property = suggestion(InboxSuggestionKind.Property, 1, 0.9);

function inboxDocument(suggestions: InboxSuggestionModel[]): InboxListItemModel {
  return {
    id: 7, fileName: 'invoice-42.pdf', subject: 'Invoice', fromAddress: 'billing@example.com',
    receivedAt: new Date(Date.UTC(2026, 9, 3, 8, 30)), readyBy: null, status: InboxDocumentStatus.Ready,
    failureReason: null, reviewedByMicroting: false, suggestions,
  };
}

describe('InboxReviewDialogComponent folder and date rows', () => {
  let fixture: ComponentFixture<InboxReviewDialogComponent>;
  let component: InboxReviewDialogComponent;
  let inboxService: {getPdf: Mock; file: Mock; undo: Mock; reject: Mock};
  let dialogRef: {close: Mock};

  // Folder 5 is the default folder: it must never be preselected on its own.
  function setup(suggestions: InboxSuggestionModel[], folders = [folder(3), folder(5, true), folder(9)]): void {
    inboxService = {
      getPdf: vi.fn(() => EMPTY),
      file: vi.fn(() => of({success: true, message: ''})),
      undo: vi.fn(),
      reject: vi.fn(),
    };
    dialogRef = {close: vi.fn()};
    TestBed.configureTestingModule({
      imports: [InboxReviewDialogComponent, TranslateModule.forRoot()],
      schemas: [NO_ERRORS_SCHEMA],
      providers: [
        {provide: MAT_DIALOG_DATA, useValue: inboxDocument([property, ...suggestions])},
        {provide: MatDialogRef, useValue: dialogRef},
        {provide: BackendConfigurationPnInboxService, useValue: inboxService},
        {
          provide: BackendConfigurationPnPropertiesService,
          useValue: {getAllPropertiesDictionary: () => of({success: true, message: '', model: [{id: 1, name: 'Example Farm'}]})},
        },
        {provide: BackendConfigurationPnFileTagsService, useValue: {getTags: () => of({success: true, message: '', model: []})}},
        {
          provide: BackendConfigurationPnArchiveFoldersService,
          useValue: {getFolders: () => of({success: true, message: '', model: folders})},
        },
      ],
    });
    // Keep the scope these specs were written for (the old TestBed module had only
    // CommonModule + these imports): the component's real Material/forms imports
    // would make them test Material instead of this component.
    TestBed.overrideComponent(InboxReviewDialogComponent, {set: {imports: [CommonModule, TranslateModule], schemas: [NO_ERRORS_SCHEMA]}});
    fixture = TestBed.createComponent(InboxReviewDialogComponent);
    component = fixture.componentInstance;
    detectChanges(fixture);
  }

  const el = (selector: string): HTMLElement | null => fixture.nativeElement.querySelector(selector);
  const button = (selector: string): HTMLButtonElement => el(selector) as HTMLButtonElement;
  const folderToggle = (id: number): HTMLButtonElement => button(`button.inbox-choice[data-kind="folder"][data-target-id="${id}"]`);
  const filedBody = (): FileInboxDocumentModel => inboxService.file.mock.calls[0][1];

  afterEach(() => fixture?.destroy());

  describe('folder', () => {
    it('preselects a confident suggestion that points at a live folder', () => {
      setup([suggestion(InboxSuggestionKind.Folder, 9, 0.8)]);
      expect(component.selectedFolderId).toBe(9);
      expect(folderToggle(9).getAttribute('aria-pressed')).toBe('true');
      expect(el('#inboxFolderRequired')).toBeNull();
    });

    it('a suggestion for a folder that no longer exists is hidden; nothing is preselected', () => {
      setup([suggestion(InboxSuggestionKind.Folder, 42, 0.9)]);
      expect(component.selectedFolderId).toBeNull();
      expect(folderToggle(42)).toBeNull();
      expect(el('#inboxFolderRequired').textContent).toContain('No matching folder was found. Choose one before you file.');
      expect(button('#inboxFileBtn').disabled).toBe(true);
    });

    it('a weak suggestion is not preselected; its toggle selects it', () => {
      setup([suggestion(InboxSuggestionKind.Folder, 3, 0.3)]);
      expect(component.selectedFolderId).toBeNull();
      expect(el('#inboxFolderRequired').textContent).toContain('Choose a folder');
      expect(folderToggle(3).getAttribute('aria-pressed')).toBe('false');

      folderToggle(3).click();
      detectChanges(fixture);
      expect(component.selectedFolderId).toBe(3);
      expect(folderToggle(3).getAttribute('aria-pressed')).toBe('true');
      expect(el('#inboxFolderRequired')).toBeNull();
    });

    it('requires a folder: filing is blocked and the hint shown until one is chosen', () => {
      setup([]);
      expect(component.selectedFolderId).toBeNull();
      expect(component.canFile).toBe(false);
      expect(button('#inboxFileBtn').disabled).toBe(true);
      expect(el('#inboxFolderRequired')).not.toBeNull();

      component.fileIt();
      expect(inboxService.file).not.toHaveBeenCalled();

      component.selectedFolderId = 3;
      detectChanges(fixture);
      expect(component.canFile).toBe(true);
      expect(button('#inboxFileBtn').disabled).toBe(false);
      expect(el('#inboxFolderRequired')).toBeNull();
    });

    it('a refused folder (success=false) keeps the dialog open and the document unfiled', () => {
      setup([]);
      component.selectedFolderId = 3;
      inboxService.file.mockReturnValue(of({success: false, message: 'Mappen findes ikke'}));
      component.fileIt();
      expect(inboxService.file).toHaveBeenCalled();
      expect(component.filed).toBe(false);
      expect(component.busy).toBe(false);
      expect(dialogRef.close).not.toHaveBeenCalled();
    });

    it('an empty completion (a swallowed 404) does not leave the buttons disabled', () => {
      setup([]);
      component.selectedFolderId = 3;
      inboxService.file.mockReturnValue(EMPTY);
      component.fileIt();
      expect(inboxService.file).toHaveBeenCalled();
      expect(component.busy).toBe(false);
      expect(component.filed).toBe(false);
    });
  });

  describe('document date', () => {
    // The host's DateInterceptor delivers targetDate as a Date read as UTC midnight.
    const dated = (): InboxSuggestionModel => suggestion(InboxSuggestionKind.DocumentDate, 0, 0.7, {targetDate: new Date(Date.UTC(2026, 9, 3))});

    it('a kept suggestion files its day as not approved', () => {
      setup([dated()]);
      expect(component.documentDate).toBe('2026-10-03');
      expect(el('#inboxDateApproveBtn')).not.toBeNull();
      expect(el('#inboxDateApproved')).toBeNull();

      component.selectedFolderId = 9;
      component.fileIt();
      expect(filedBody()).toEqual({
        name: 'invoice-42', propertyIds: [1], tagIds: [], folderId: 9,
        documentDate: '2026-10-03', documentDateApproved: false,
      });
    });

    it('Approve approves the suggested day', () => {
      setup([dated()]);
      button('#inboxDateApproveBtn').click();
      detectChanges(fixture);
      expect(el('#inboxDateApproved')).not.toBeNull();
      expect(el('#inboxDateApproveBtn')).toBeNull();

      component.selectedFolderId = 9;
      component.fileIt();
      expect(filedBody()).toEqual(expect.objectContaining({documentDate: '2026-10-03', documentDateApproved: true}));
    });

    it('Change date sets another day, which is approved', () => {
      setup([dated()]);
      button('#inboxDateEditBtn').click();
      detectChanges(fixture);
      expect(el('#inboxDateInput')).not.toBeNull();

      component.setDate('2026-09-30');
      button('#inboxDateDoneBtn').click();
      detectChanges(fixture);
      expect(el('#inboxDateInput')).toBeNull();
      expect(el('#inboxDateApproved')).not.toBeNull();

      component.selectedFolderId = 9;
      component.fileIt();
      expect(filedBody()).toEqual(expect.objectContaining({documentDate: '2026-09-30', documentDateApproved: true}));
    });

    it('clearing the date files without one', () => {
      setup([dated()]);
      component.dateApproved = true;
      component.setDate('');
      detectChanges(fixture);
      expect(el('#inboxDateValue').textContent).toContain('No date');

      component.selectedFolderId = 9;
      component.fileIt();
      expect(filedBody()).toEqual(expect.objectContaining({documentDate: null, documentDateApproved: false}));
    });

    it('once filed, the folder toggle and the date controls are disabled', () => {
      setup([dated(), suggestion(InboxSuggestionKind.Folder, 9, 0.8)]);
      component.fileIt();
      detectChanges(fixture);
      expect(component.filed).toBe(true);
      expect(folderToggle(9).disabled).toBe(true);
      expect(button('#inboxDateApproveBtn').disabled).toBe(true);
      expect(button('#inboxDateEditBtn').disabled).toBe(true);
    });

    it('without a suggestion there is no date and no Approve', () => {
      setup([]);
      expect(component.documentDate).toBeNull();
      expect(el('#inboxDateApproveBtn')).toBeNull();

      component.selectedFolderId = 9;
      component.fileIt();
      expect(filedBody()).toEqual(expect.objectContaining({documentDate: null, documentDateApproved: false}));
    });
  });
});
