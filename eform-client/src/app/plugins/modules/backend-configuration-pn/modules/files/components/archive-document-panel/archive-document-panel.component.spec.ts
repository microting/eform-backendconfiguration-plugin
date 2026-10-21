import {describe, it, expect, beforeEach, afterEach, vi} from 'vitest';
import type {Mock} from 'vitest';
// ng2-pdf-viewer assigns to the pdfjs-dist module namespace when it loads, which
// throws under the unit-test builder (ESM namespaces are not extensible), and
// pdfjs-dist cannot be mocked from inside the package. No spec renders a PDF.
vi.mock('ng2-pdf-viewer', () => ({PdfViewerModule: class {}, PdfViewerComponent: class {}}));
import {CommonModule} from '@angular/common';
import {NO_ERRORS_SCHEMA} from '@angular/core';
import {ComponentFixture, TestBed} from '@angular/core/testing';
import {By} from '@angular/platform-browser';
import {A11yModule, CdkTrapFocus} from '@angular/cdk/a11y';
import {TranslateModule} from '@ngx-translate/core';
import {EMPTY, Observable, Subject, of} from 'rxjs';
import {CommonDictionaryModel, OperationResult, SharedTagModel} from 'src/app/common/models';
import {ArchiveDocumentPanelComponent} from './archive-document-panel.component';
import {ArchivePanelValue} from '../../../../helpers';
import {ArchiveFolderModel, DocumentDateState, FileModel} from '../../../../models';
import {BackendConfigurationPnFilesService} from '../../../../services';
import {ArchiveMoveFlowService} from '../../services/archive-move-flow.service';

const ledelse = {id: 5, name: 'Ledelse'};
const vagt = {id: 9, name: 'Vagt'};
const forsyning: SharedTagModel = {id: 3, name: 'Forsyning', description: '', isLocked: false};
const ledelseTag: CommonDictionaryModel = {id: 5, name: 'Ledelse', description: ''};
// readGroups are the EFFECTIVE groups (FileModel): the folder's, since the document has no own access.
const file: FileModel = {
  id: 7, fileName: 'Faktura 118', fileExtension: 'pdf', properties: [1], tags: [forsyning],
  folderId: 10, folderPath: ['Drift'], documentDate: '2026-10-03T00:00:00',
  documentDateState: DocumentDateState.Suggested, readGroups: [ledelse], ownAccess: false,
};
const folders: ArchiveFolderModel[] = [
  {id: 10, name: 'Drift', parentId: null, isDefault: false, count: 1, readGroups: [ledelse], ownGroups: true, restricted: true},
  {id: 11, name: 'Arkiv', parentId: null, isDefault: true, count: 0, readGroups: [], ownGroups: false, restricted: false},
];

describe('ArchiveDocumentPanelComponent', () => {
  let filesService: any;
  let moveFlow: {move: Mock};
  let saved: Mock;
  let closed: Mock;
  let panel: ArchiveDocumentPanelComponent;

  const edit = (patch: Partial<ArchivePanelValue>) => (panel.edited = {...panel.edited!, ...patch});
  const reloadWith = (model: FileModel) => {
    filesService.getFile.mockReturnValue(of({success: true, model}));
    panel.ngOnChanges({fileId: {} as any});
  };

  beforeEach(() => {
    filesService = {
      getFile: vi.fn().mockReturnValue(of({success: true, model: file})),
      getPdfFile: vi.fn().mockReturnValue(EMPTY),
      updateFileName: vi.fn().mockReturnValue(of({success: true})),
      updateFileTags: vi.fn().mockReturnValue(of({success: true})),
      setDocumentDate: vi.fn().mockReturnValue(of({success: true})),
      setFilesAccess: vi.fn().mockReturnValue(of({success: true})),
    };
    moveFlow = {move: vi.fn().mockReturnValue(of(true))};
    const translate = {instant: (key: string, params?: object) => (params ? `${key} ${JSON.stringify(params)}` : key)};
    panel = new ArchiveDocumentPanelComponent(filesService, moveFlow as any, translate as any);
    panel.fileId = 7;
    panel.folders = folders;
    panel.workerTags = [ledelseTag];
    panel.isAdmin = true;
    saved = vi.fn();
    panel.saved.subscribe(saved);
    closed = vi.fn();
    panel.closed.subscribe(closed);
    panel.ngOnChanges({fileId: {} as any});
  });

  it('loads the document into an editable copy; the date as yyyy-MM-dd', () => {
    expect(filesService.getFile).toHaveBeenCalledWith(7);
    expect(panel.edited?.documentDate).toBe('2026-10-03');
    expect(panel.edited?.documentDateState).toBe(DocumentDateState.Suggested);
    expect(panel.canSave).toBe(false);
  });

  it('reads the date the host DateInterceptor produced (a Date at midnight UTC) as the same day (C1)', () => {
    reloadWith({...file, documentDate: new Date(Date.UTC(2026, 9, 3))});
    expect(panel.edited?.documentDate).toBe('2026-10-03');
    panel.approveDate();
    panel.save(false);
    expect(filesService.setDocumentDate).toHaveBeenCalledWith(7, {date: '2026-10-03'});
  });

  it('Godkend sends the suggested date, which approves it', () => {
    panel.approveDate();
    panel.save(false);
    expect(filesService.setDocumentDate).toHaveBeenCalledWith(7, {date: '2026-10-03'});
    expect(filesService.updateFileName).not.toHaveBeenCalled();
    expect(saved).toHaveBeenCalledWith({fileId: 7, next: false});
    expect(filesService.getFile).toHaveBeenCalledTimes(2); // reloaded, so the panel shows the saved state
  });

  it('Ret with an emptied date clears it', () => {
    panel.setDate('');
    panel.save(false);
    expect(filesService.setDocumentDate).toHaveBeenCalledWith(7, {date: null});
  });

  it('Ret with a new date sends that date, which approves it', () => {
    panel.setDate('2026-10-05');
    expect(panel.edited?.documentDateState).toBe(DocumentDateState.Approved);
    panel.save(false);
    expect(filesService.setDocumentDate).toHaveBeenCalledWith(7, {date: '2026-10-05'});
  });

  it('a plain Save keeps the current view and re-reads only the document data', () => {
    const reply = new Subject<{success: boolean; model: FileModel}>();
    filesService.getFile.mockReturnValue(reply);
    panel.approveDate();
    panel.save(false);
    expect(filesService.getFile).toHaveBeenCalledTimes(2);
    expect(panel.edited).not.toBeNull();
    expect(filesService.getPdfFile).toHaveBeenCalledTimes(1);
    reply.next({success: true, model: {...file, documentDateState: DocumentDateState.Approved}});
    reply.complete();
    expect(panel.original?.documentDateState).toBe(DocumentDateState.Approved);
    expect(panel.canSave).toBe(false);
  });

  it('is busy while a save runs', () => {
    const answer = new Subject<OperationResult>();
    filesService.setDocumentDate.mockReturnValue(answer);
    panel.approveDate();
    panel.save(false);
    expect(panel.busy).toBe(true);
    expect(panel.canSave).toBe(false);
    answer.next({success: true} as OperationResult);
    answer.complete();
    expect(panel.busy).toBe(false);
  });

  it('a save that ends after the selection moved on never says "next" for the old document', () => {
    const answer = new Subject<OperationResult>();
    filesService.setDocumentDate.mockReturnValue(answer);
    panel.hasNext = true;
    panel.approveDate();
    panel.save(true);
    panel.fileId = 8;
    panel.ngOnChanges({fileId: {} as any});
    answer.next({success: true} as OperationResult);
    answer.complete();
    expect(saved).toHaveBeenCalledWith({fileId: 7, next: false});
    expect(filesService.getFile).toHaveBeenLastCalledWith(8);
    expect(filesService.getFile).toHaveBeenCalledTimes(2); // only the new document's load, no reload of 7
  });

  it('closes when the document is gone or no longer readable (getFile completes empty)', () => {
    filesService.getFile.mockReturnValue(EMPTY);
    panel.ngOnChanges({fileId: {} as any});
    expect(closed).toHaveBeenCalledTimes(1);
    expect(panel.edited).toBeNull();
  });

  it('refreshes the access items when only the worker groups change', () => {
    reloadWith({...file, ownAccess: true, readGroups: [vagt]});
    panel.workerTags = [ledelseTag, {id: 9, name: 'Vagt', description: ''}];
    panel.ngOnChanges({workerTags: {} as any});
    expect(panel.accessItems).toEqual([{id: 5, name: 'Ledelse', description: ''}, {id: 9, name: 'Vagt', description: ''}]);
  });

  it('keeps a selected stale group when worker groups arrive after "Own access" was clicked', () => {
    reloadWith({...file, ownAccess: false, readGroups: []});
    panel.workerTags = [];
    panel.useOwnAccess();
    edit({readTagIds: [9]});
    panel.workerTags = [ledelseTag];
    panel.ngOnChanges({workerTags: {} as any});
    expect(panel.accessItems.map(g => g.id)).toEqual([5, 9]);
    expect(panel.edited?.readTagIds).toEqual([9]);
  });

  it('"Gem og næste" without changes moves on without calling the API', () => {
    panel.hasNext = true;
    panel.save(true);
    expect(saved).toHaveBeenCalledWith({fileId: 7, next: true});
    expect(filesService.updateFileName).not.toHaveBeenCalled();
    expect(moveFlow.move).not.toHaveBeenCalled();
  });

  it('saves name, properties and tags, then moves the folder last', () => {
    edit({name: 'Faktura 118 Vand', tagIds: []});
    edit({folderId: 11});
    panel.save(true);
    expect(filesService.updateFileName).toHaveBeenCalledWith({id: 7, newName: 'Faktura 118 Vand', propertyIds: [1]});
    expect(filesService.updateFileTags).toHaveBeenCalledWith({id: 7, tags: []});
    // The effective groups, so the move flow sees that leaving Drift opens the document (D11).
    expect(moveFlow.move).toHaveBeenCalledWith([{id: 7, folderId: 10, ownAccess: false, readGroups: [ledelse]}], folders, 11, true);
    expect(saved).toHaveBeenCalledWith({fileId: 7, next: true});
  });

  it('after "use the folder\'s access" the move compares against the old folder\'s groups', () => {
    reloadWith({...file, ownAccess: true, readGroups: [vagt]});
    panel.useFolderAccess();
    edit({folderId: 11});
    panel.save(false);
    expect(filesService.setFilesAccess).toHaveBeenCalledWith({fileIds: [7], ownAccess: false, tagIds: []});
    expect(moveFlow.move).toHaveBeenCalledWith([{id: 7, folderId: 10, ownAccess: false, readGroups: [ledelse]}], folders, 11, true);
  });

  it('a failing step stops the rest, reloads the document and refreshes the table', () => {
    filesService.updateFileName.mockReturnValue(of({success: false}));
    edit({name: 'Faktura 118 Vand', tagIds: []});
    panel.save(true);
    expect(filesService.updateFileTags).not.toHaveBeenCalled();
    expect(filesService.getFile).toHaveBeenCalledTimes(2);
    expect(saved).toHaveBeenCalledWith({fileId: 7, next: false});
  });

  it('a step that completes empty (a 404 the host swallowed) counts as not saved and stops the rest (C4)', () => {
    filesService.updateFileName.mockReturnValue(EMPTY);
    edit({name: 'Faktura 118 Vand', tagIds: []});
    edit({folderId: 11});
    panel.hasNext = true;
    panel.save(true);
    expect(filesService.updateFileTags).not.toHaveBeenCalled();
    expect(moveFlow.move).not.toHaveBeenCalled();
    expect(filesService.getFile).toHaveBeenCalledTimes(2);
    expect(saved).toHaveBeenCalledTimes(1);
    expect(saved).toHaveBeenCalledWith({fileId: 7, next: false});
    expect(panel.busy).toBe(false);
  });

  it('an empty access answer keeps the folder move from running (C4)', () => {
    filesService.setFilesAccess.mockReturnValue(EMPTY);
    panel.useOwnAccess();
    edit({folderId: 11});
    panel.save(false);
    expect(moveFlow.move).not.toHaveBeenCalled();
    expect(saved).toHaveBeenCalledWith({fileId: 7, next: false});
  });

  it('a cancelled folder move is not saved: no next, reloaded', () => {
    moveFlow.move.mockReturnValue(of(false));
    edit({folderId: 11});
    panel.save(true);
    expect(filesService.getFile).toHaveBeenCalledTimes(2);
    expect(saved).toHaveBeenCalledWith({fileId: 7, next: false});
  });

  it('own access starts from the groups the document reads with today', () => {
    panel.useOwnAccess();
    expect(panel.edited?.readTagIds).toEqual([5]);
    panel.save(false);
    expect(filesService.setFilesAccess).toHaveBeenCalledWith({fileIds: [7], ownAccess: true, tagIds: [5]});
  });

  it('keeps an own group without live members selectable instead of dropping it', () => {
    reloadWith({...file, ownAccess: true, readGroups: [vagt]});
    expect(panel.accessItems.map(i => i.id)).toEqual([5, 9]);
    expect(panel.accessItems[1].name).toContain('no members');
    expect(panel.edited?.readTagIds).toEqual([9]);
  });

  it('own access from an inherited group without live members keeps that group too', () => {
    panel.folders = [{...folders[0], readGroups: [vagt]}, folders[1]];
    reloadWith({...file, readGroups: [vagt]});
    panel.useOwnAccess();
    expect(panel.edited?.readTagIds).toEqual([9]);
    expect(panel.accessItems.map(i => i.id)).toEqual([5, 9]);
    expect(panel.ownGroupsLabel).toContain('Vagt');
  });

  it('shows where the access is inherited from', () => {
    expect(panel.inheritedLabel).toBe('Inherits from {{folder}}: {{who}} {"folder":"Drift","who":"Ledelse"}');
    edit({folderId: 11});
    expect(panel.inheritedLabel).toBe('Inherits from {{folder}}: {{who}} {"folder":"Arkiv","who":"everyone with archive access"}');
  });

  it('shows only the groups when they come from a parent folder that is not visible', () => {
    // Subfolder 12 inherits from parent 99, which this user does not get in the folder list.
    panel.folders = [...folders, {id: 12, name: 'Fakturaer', parentId: 99, isDefault: false, count: 1, readGroups: [ledelse], ownGroups: false, restricted: true}];
    edit({folderId: 12});
    expect(panel.inheritedLabel).toBe('Read access: {{who}} {"who":"Ledelse"}');
  });

  it('non-admins see who reads the document, without controls', () => {
    panel.isAdmin = false;
    expect(panel.readAccessLabel).toBe('Read access: {{who}} {"who":"Ledelse"}');
  });
});

describe('ArchiveDocumentPanelComponent as a drawer (below 1100 px)', () => {
  let fixture: ComponentFixture<ArchiveDocumentPanelComponent>;
  let row: HTMLElement;
  let closed: Mock;
  const aside = (): HTMLElement => fixture.nativeElement.querySelector('#archiveDocumentPanel');
  const closeBtn = (): HTMLElement => fixture.nativeElement.querySelector('#archivePanelCloseBtn');

  /** The row that opened the panel has focus, as after a click or Enter on it. FormsModule is left out on purpose:
   * the body's ngModel bindings stay inert, so only the panel's frame is under test. */
  function render(drawer: boolean): void {
    row = document.createElement('div');
    row.tabIndex = 0;
    document.body.appendChild(row);
    row.focus();
    TestBed.configureTestingModule({
      imports: [ArchiveDocumentPanelComponent, A11yModule, TranslateModule.forRoot()],
      providers: [
        {provide: BackendConfigurationPnFilesService, useValue: {getFile: () => of({success: true, model: file}), getPdfFile: () => EMPTY}},
        {provide: ArchiveMoveFlowService, useValue: {move: vi.fn()}},
      ],
      schemas: [NO_ERRORS_SCHEMA],
    });
    // Keep the scope these specs were written for (the old TestBed module had only
    // CommonModule + these imports): the component's real Material/forms imports
    // would make them test Material instead of this component.
    TestBed.overrideComponent(ArchiveDocumentPanelComponent, {set: {imports: [CommonModule, A11yModule, TranslateModule], schemas: [NO_ERRORS_SCHEMA]}});
    fixture = TestBed.createComponent(ArchiveDocumentPanelComponent);
    fixture.componentRef.setInput('fileId', 7);
    fixture.componentRef.setInput('drawer', drawer);
    closed = vi.fn();
    fixture.componentInstance.closed.subscribe(closed);
    fixture.detectChanges();
  }

  afterEach(() => {
    fixture?.destroy();
    row?.remove();
  });

  it('is a modal dialog named by the document, traps focus and starts on the close button', () => {
    render(true);
    expect(aside().getAttribute('role')).toBe('dialog');
    expect(aside().getAttribute('aria-modal')).toBe('true');
    expect(aside().getAttribute('aria-labelledby')).toBe('archivePanelTitle');
    expect(fixture.debugElement.query(By.directive(CdkTrapFocus)).injector.get(CdkTrapFocus).enabled).toBe(true);
    expect(document.activeElement).toBe(closeBtn());
  });

  it('closes on Escape, but not when a control inside already handled it', () => {
    render(true);
    const handled = new KeyboardEvent('keydown', {key: 'Escape', bubbles: true, cancelable: true});
    handled.preventDefault();
    closeBtn().dispatchEvent(handled);
    expect(closed).not.toHaveBeenCalled();
    closeBtn().dispatchEvent(new KeyboardEvent('keydown', {key: 'Escape', bubbles: true, cancelable: true}));
    expect(closed).toHaveBeenCalledTimes(1);
  });

  it('gives focus back to the row that opened it when it closes', () => {
    render(true);
    expect(document.activeElement).not.toBe(row);
    fixture.destroy();
    expect(document.activeElement).toBe(row);
  });

  it('side by side (1100 px and up) stays a plain region: no dialog role, no trap, focus stays on the row', () => {
    render(false);
    expect(aside().getAttribute('role')).toBeNull();
    expect(aside().getAttribute('aria-modal')).toBeNull();
    expect(fixture.debugElement.query(By.directive(CdkTrapFocus)).injector.get(CdkTrapFocus).enabled).toBe(false);
    expect(document.activeElement).toBe(row);
    row.blur();
    fixture.destroy();
    expect(document.activeElement).not.toBe(row);
  });
});

describe('ArchiveDocumentPanelComponent preview by file kind', () => {
  let fixture: ComponentFixture<ArchiveDocumentPanelComponent>;
  let createObjectURL: Mock;
  let revokeObjectURL: Mock;
  const original = {create: URL.createObjectURL, revoke: URL.revokeObjectURL};
  const q = (selector: string): HTMLElement | null => fixture.nativeElement.querySelector(selector);

  function render(fileExtension: string, bytes$: Observable<Blob> = of(new Blob(['x']))): void {
    TestBed.configureTestingModule({
      imports: [ArchiveDocumentPanelComponent, TranslateModule.forRoot()],
      providers: [
        {
          provide: BackendConfigurationPnFilesService,
          useValue: {
            getFile: () => of({success: true, model: {...file, fileName: 'example-file', fileExtension}}),
            getPdfFile: vi.fn().mockReturnValue(bytes$),
          },
        },
        {provide: ArchiveMoveFlowService, useValue: {move: vi.fn()}},
      ],
      schemas: [NO_ERRORS_SCHEMA],
    });
    TestBed.overrideComponent(ArchiveDocumentPanelComponent, {set: {imports: [CommonModule, A11yModule, TranslateModule], schemas: [NO_ERRORS_SCHEMA]}});
    fixture = TestBed.createComponent(ArchiveDocumentPanelComponent);
    fixture.componentRef.setInput('fileId', 7);
    fixture.detectChanges();
  }

  beforeEach(() => {
    createObjectURL = vi.fn().mockReturnValue('blob:example-image');
    revokeObjectURL = vi.fn();
    Object.assign(URL, {createObjectURL, revokeObjectURL});
  });

  afterEach(() => {
    fixture?.destroy();
    Object.assign(URL, {createObjectURL: original.create, revokeObjectURL: original.revoke});
  });

  it('an image is shown in an img named by the file, with no PDF viewer and no "no preview" text', () => {
    render('png');
    const img = q('#archivePanelImagePreview') as HTMLImageElement;
    expect(img).not.toBeNull();
    expect(img.getAttribute('src')).toBe('blob:example-image');
    expect(img.getAttribute('alt')).toBe('example-file');
    expect(q('pdf-viewer')).toBeNull();
    expect(q('#archivePanelNoPreview')).toBeNull();
  });

  it('an image URL is made once per open and revoked when the panel goes away', () => {
    render('jpg');
    expect(createObjectURL).toHaveBeenCalledTimes(1);
    fixture.destroy();
    expect(revokeObjectURL).toHaveBeenCalledWith('blob:example-image');
  });

  it('an image that arrives after the panel closed makes no object URL', () => {
    const bytes = new Subject<Blob>();
    render('png', bytes);
    fixture.destroy();
    bytes.next(new Blob(['x']));
    expect(bytes.observed).toBe(false);
    expect(createObjectURL).not.toHaveBeenCalled();
  });

  it('a PDF is not shown as an image', () => {
    render('pdf');
    expect(q('#archivePanelImagePreview')).toBeNull();
    expect(q('#archivePanelNoPreview')).toBeNull();
    expect(createObjectURL).not.toHaveBeenCalled();
  });

  it('any other file says there is no preview', () => {
    render('docx');
    expect(q('#archivePanelNoPreview')?.textContent).toContain('No preview');
    expect(q('#archivePanelImagePreview')).toBeNull();
    expect(q('pdf-viewer')).toBeNull();
  });
});
