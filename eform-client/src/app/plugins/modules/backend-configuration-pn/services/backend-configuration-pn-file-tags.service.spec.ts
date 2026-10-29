import {describe, it, expect, beforeEach, vi} from 'vitest';
import type {Mock} from 'vitest';
import {of} from 'rxjs';
import {BackendConfigurationPnFileTagsService} from './backend-configuration-pn-file-tags.service';
import {passThroughAccountDisabledHandler} from './archive-account-disabled.handler.stub';

/** S3: file-tags now has the D7 filter, and rename/delete answer 403 to non-managers (D-L11). */
describe('BackendConfigurationPnFileTagsService', () => {
  let api: any;
  let handle: Mock;
  let service: BackendConfigurationPnFileTagsService;

  beforeEach(() => {
    api = {
      get: vi.fn().mockReturnValue(of({success: true, model: []})),
      post: vi.fn().mockReturnValue(of({success: true})),
      put: vi.fn().mockReturnValue(of({success: true})),
      delete: vi.fn().mockReturnValue(of({success: true})),
    };
    const stub = passThroughAccountDisabledHandler();
    handle = stub.handle;
    service = new BackendConfigurationPnFileTagsService(api, stub.handler);
  });

  it('routes every call through the account-disabled handler', () => {
    service.getTags().subscribe();
    service.getTagById(3).subscribe();
    service.createTag({name: 'Forsyning'} as any).subscribe();
    service.createTags({names: ['A', 'B']} as any).subscribe();
    service.updateTag({id: 3, name: 'Forsyning 2', description: '', isLocked: false}).subscribe();
    service.deleteTag(3).subscribe();
    expect(handle).toHaveBeenCalledTimes(6);
    expect(api.put).toHaveBeenCalledWith('api/backend-configuration-pn/file-tags', expect.objectContaining({id: 3}));
    expect(api.delete).toHaveBeenCalledWith('api/backend-configuration-pn/file-tags/3');
  });
});
