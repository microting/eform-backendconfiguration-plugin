import {of} from 'rxjs';
import {BackendConfigurationPnFilesService} from './backend-configuration-pn-files.service';
import {DocumentDateState, FileModel} from '../models';
import {serverDay} from '../modules/tail-bite/shared/tail-bite-dates';
import {passThroughAccountDisabledHandler} from './archive-account-disabled.handler.stub';

/** Wire contract of FilesController for the archive-folder additions (plan 2). */
describe('BackendConfigurationPnFilesService', () => {
  let api: any;
  let handle: jest.Mock;
  let service: BackendConfigurationPnFilesService;

  beforeEach(() => {
    api = {
      post: jest.fn().mockReturnValue(of({success: true})),
      put: jest.fn().mockReturnValue(of({success: true})),
      get: jest.fn().mockReturnValue(of({success: true})),
      delete: jest.fn().mockReturnValue(of({success: true})),
      postFormData: jest.fn().mockReturnValue(of({success: true})),
      getBlobData: jest.fn().mockReturnValue(of(new Blob([]))),
      postBlobData: jest.fn().mockReturnValue(of(new Blob([]))),
    };
    const stub = passThroughAccountDisabledHandler();
    handle = stub.handle;
    service = new BackendConfigurationPnFilesService(api, stub.handler);
  });

  it('POSTs the folder and date filters with the list request', () => {
    const request = {
      propertyIds: [], tagIds: [], dateFrom: '', dateTo: '', nameFilter: '', sort: 'Id', isSortDsc: false,
      folderId: null, dateState: DocumentDateState.Suggested,
    };
    service.getAllFiles(request).subscribe();
    expect(api.post).toHaveBeenCalledWith('api/backend-configuration-pn/files', request);
  });

  it('moveFiles PUTs files/folder', () => {
    service.moveFiles({fileIds: [1, 2], folderId: 9}).subscribe();
    expect(api.put).toHaveBeenCalledWith('api/backend-configuration-pn/files/folder', {fileIds: [1, 2], folderId: 9});
  });

  it('setDocumentDate PUTs files/{id}/date with a date-only value or null', () => {
    service.setDocumentDate(7, {date: '2026-10-03'}).subscribe();
    expect(api.put).toHaveBeenLastCalledWith('api/backend-configuration-pn/files/7/date', {date: '2026-10-03'});
    service.setDocumentDate(7, {date: null}).subscribe();
    expect(api.put).toHaveBeenLastCalledWith('api/backend-configuration-pn/files/7/date', {date: null});
  });

  it('a document date arrives as the DateInterceptor\'s Date and is sent back as its UTC day', () => {
    const file: FileModel = {
      id: 7, fileName: 'Faktura 1001.pdf', properties: [3], tags: [], folderId: 9, folderPath: ['Drift', 'Fakturaer'],
      // "2026-10-03T00:00:00" from the server, read by the host's DateInterceptor as midnight UTC.
      documentDate: new Date(Date.UTC(2026, 9, 3)),
      documentDateState: DocumentDateState.Suggested, readGroups: [], ownAccess: false,
    };
    api.get.mockReturnValue(of({success: true, model: file}));

    let received: FileModel | undefined;
    service.getFile(7).subscribe(result => received = result.model);
    expect(received.documentDate).toBeInstanceOf(Date);

    service.setDocumentDate(7, {date: serverDay(received.documentDate)}).subscribe();
    expect(api.put).toHaveBeenLastCalledWith('api/backend-configuration-pn/files/7/date', {date: '2026-10-03'});
  });

  it('setFilesAccess PUTs files/access', () => {
    service.setFilesAccess({fileIds: [7], ownAccess: true, tagIds: [5]}).subscribe();
    expect(api.put).toHaveBeenCalledWith('api/backend-configuration-pn/files/access', {fileIds: [7], ownAccess: true, tagIds: [5]});
  });

  it('updateFileProperties PUTs files/properties', () => {
    service.updateFileProperties({id: 7, properties: [3]}).subscribe();
    expect(api.put).toHaveBeenCalledWith('api/backend-configuration-pn/files/properties', {id: 7, properties: [3]});
  });

  it('routes every call, JSON and blob, through the account-disabled handler', () => {
    service.getAllFiles({
      propertyIds: [], tagIds: [], dateFrom: '', dateTo: '', nameFilter: '', sort: 'Id', isSortDsc: false,
    }).subscribe();
    service.getFile(7).subscribe();
    service.updateFileName({id: 7, newName: 'Faktura 1002', propertyIds: [3]}).subscribe();
    service.updateFileTags({id: 7, tags: [5]}).subscribe();
    service.updateFileProperties({id: 7, properties: [3]}).subscribe();
    service.createFiles({filesForCreate: []}).subscribe();
    service.deleteFile(7).subscribe();
    service.moveFiles({fileIds: [7], folderId: 9}).subscribe();
    service.setDocumentDate(7, {date: null}).subscribe();
    service.setFilesAccess({fileIds: [7], ownAccess: false, tagIds: []}).subscribe();
    service.downloadFiles({fileIds: [7], archiveName: 'x'}).subscribe();
    service.getPdfFile(7).subscribe();
    expect(handle).toHaveBeenCalledTimes(12);
  });
});
