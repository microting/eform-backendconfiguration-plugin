import {EMPTY, Subject, of} from 'rxjs';
import {CommonDictionaryModel, OperationResult, SharedTagModel} from 'src/app/common/models';
import {ArchiveDocumentPanelComponent} from './archive-document-panel.component';
import {ArchivePanelValue} from '../../../../helpers';
import {ArchiveFolderModel, DocumentDateState, FileModel} from '../../../../models';

const ledelse = {id: 5, name: 'Ledelse'};
const vagt = {id: 9, name: 'Vagt'};
const forsyning: SharedTagModel = {id: 3, name: 'Forsyning', description: '', isLocked: false};
const ledelseTag: CommonDictionaryModel = {id: 5, name: 'Ledelse', description: ''};
// readGroups are the EFFECTIVE groups (FileModel): the folder's, since the document has no own access.
const file: FileModel = {
  id: 7, fileName: 'Faktura 118', properties: [1], tags: [forsyning],
  folderId: 10, folderPath: ['Drift'], documentDate: '2026-10-03T00:00:00',
  documentDateState: DocumentDateState.Suggested, readGroups: [ledelse], ownAccess: false,
};
const folders: ArchiveFolderModel[] = [
  {id: 10, name: 'Drift', parentId: null, isDefault: false, count: 1, readGroups: [ledelse], ownGroups: true, restricted: true},
  {id: 11, name: 'Arkiv', parentId: null, isDefault: true, count: 0, readGroups: [], ownGroups: false, restricted: false},
];

describe('ArchiveDocumentPanelComponent', () => {
  let filesService: any;
  let moveFlow: {move: jest.Mock};
  let saved: jest.Mock;
  let closed: jest.Mock;
  let panel: ArchiveDocumentPanelComponent;

  const edit = (patch: Partial<ArchivePanelValue>) => (panel.edited = {...panel.edited!, ...patch});
  const reloadWith = (model: FileModel) => {
    filesService.getFile.mockReturnValue(of({success: true, model}));
    panel.ngOnChanges({fileId: {} as any});
  };

  beforeEach(() => {
    filesService = {
      getFile: jest.fn().mockReturnValue(of({success: true, model: file})),
      getPdfFile: jest.fn().mockReturnValue(EMPTY),
      updateFileName: jest.fn().mockReturnValue(of({success: true})),
      updateFileTags: jest.fn().mockReturnValue(of({success: true})),
      setDocumentDate: jest.fn().mockReturnValue(of({success: true})),
      setFilesAccess: jest.fn().mockReturnValue(of({success: true})),
    };
    moveFlow = {move: jest.fn().mockReturnValue(of(true))};
    const translate = {instant: (key: string, params?: object) => (params ? `${key} ${JSON.stringify(params)}` : key)};
    panel = new ArchiveDocumentPanelComponent(filesService, moveFlow as any, translate as any);
    panel.fileId = 7;
    panel.folders = folders;
    panel.workerTags = [ledelseTag];
    panel.isAdmin = true;
    saved = jest.fn();
    panel.saved.subscribe(saved);
    closed = jest.fn();
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
