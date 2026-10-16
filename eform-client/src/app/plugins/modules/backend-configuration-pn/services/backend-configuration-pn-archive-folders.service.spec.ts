import {of} from 'rxjs';
import {
  BackendConfigurationPnArchiveFoldersMethods,
  BackendConfigurationPnArchiveFoldersService,
} from './backend-configuration-pn-archive-folders.service';
import {passThroughAccountDisabledHandler} from './archive-account-disabled.handler.stub';

/** Wire contract of ArchiveFoldersController (plan 2). */
describe('BackendConfigurationPnArchiveFoldersService', () => {
  let api: any;
  let handle: jest.Mock;
  let service: BackendConfigurationPnArchiveFoldersService;

  beforeEach(() => {
    api = {
      get: jest.fn().mockReturnValue(of({success: true, model: []})),
      post: jest.fn().mockReturnValue(of({success: true})),
      put: jest.fn().mockReturnValue(of({success: true})),
      delete: jest.fn().mockReturnValue(of({success: true})),
    };
    const stub = passThroughAccountDisabledHandler();
    handle = stub.handle;
    service = new BackendConfigurationPnArchiveFoldersService(api, stub.handler);
  });

  it('GETs the tree without a query when no property is selected', () => {
    service.getFolders([]).subscribe();
    expect(api.get).toHaveBeenCalledWith('api/backend-configuration-pn/archive-folders');
    expect(BackendConfigurationPnArchiveFoldersMethods.ArchiveFolders).toBe('api/backend-configuration-pn/archive-folders');
  });

  it('repeats propertyIds once per property', () => {
    service.getFolders([3, 8]).subscribe();
    expect(api.get).toHaveBeenCalledWith('api/backend-configuration-pn/archive-folders?propertyIds=3&propertyIds=8');
  });

  it('creates, updates, deletes and restricts by id', () => {
    service.createFolder({name: 'Drift', parentId: null}).subscribe();
    expect(api.post).toHaveBeenCalledWith('api/backend-configuration-pn/archive-folders', {name: 'Drift', parentId: null});

    service.updateFolder(4, {moveToTop: true}).subscribe();
    expect(api.put).toHaveBeenCalledWith('api/backend-configuration-pn/archive-folders/4', {moveToTop: true});

    service.setFolderAccess(4, {tagIds: [5, 6]}).subscribe();
    expect(api.put).toHaveBeenCalledWith('api/backend-configuration-pn/archive-folders/4/access', {tagIds: [5, 6]});

    service.deleteFolder(4).subscribe();
    expect(api.delete).toHaveBeenCalledWith('api/backend-configuration-pn/archive-folders/4');
  });

  it('routes every call through the account-disabled handler', () => {
    service.getFolders([]).subscribe();
    service.createFolder({name: 'Drift', parentId: null}).subscribe();
    service.updateFolder(4, {name: 'Drift 2'}).subscribe();
    service.setFolderAccess(4, {tagIds: []}).subscribe();
    service.deleteFolder(4).subscribe();
    expect(handle).toHaveBeenCalledTimes(5);
  });
});
