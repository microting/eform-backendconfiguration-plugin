import {of} from 'rxjs';
import {
  BackendConfigurationPnInboxMethods,
  BackendConfigurationPnInboxService,
} from './backend-configuration-pn-inbox.service';
import {InboxDocumentStatus, InboxSenderRuleKind, InboxSuggestionKind, InboxSuggestionModel} from '../models';
import {serverDay} from '../modules/tail-bite/shared/tail-bite-dates';
import {passThroughAccountDisabledHandler} from './archive-account-disabled.handler.stub';

/**
 * Pins the wire contract of the Indbakke service against InboxController: URL, verb and body of each
 * call. What happens on the server is covered by the C# integration tests; nothing here can see it.
 */
describe('BackendConfigurationPnInboxService', () => {
  let service: BackendConfigurationPnInboxService;
  let apiBaseServiceSpy: any;
  let handle: jest.Mock;

  beforeEach(() => {
    apiBaseServiceSpy = {
      get: jest.fn(),
      getNoToast: jest.fn(),
      post: jest.fn(),
      put: jest.fn(),
      getBlobData: jest.fn(),
    };
    apiBaseServiceSpy.get.mockReturnValue(of({success: true, model: []}));
    apiBaseServiceSpy.getNoToast.mockReturnValue(of({success: true, model: {}}));
    apiBaseServiceSpy.post.mockReturnValue(of({success: true}));
    apiBaseServiceSpy.put.mockReturnValue(of({success: true}));
    apiBaseServiceSpy.getBlobData.mockReturnValue(of(new Blob([])));
    const stub = passThroughAccountDisabledHandler();
    handle = stub.handle;
    service = new BackendConfigurationPnInboxService(apiBaseServiceSpy, stub.handler);
  });

  describe('list', () => {
    it('GETs the inbox route with status and trimmed search as query params', () => {
      service.list(InboxDocumentStatus.Ready, '  invoice ').subscribe();

      const [url, params] = apiBaseServiceSpy.get.mock.lastCall;
      expect(url).toBe('api/backend-configuration-pn/inbox');
      expect(url).toBe(BackendConfigurationPnInboxMethods.Inbox);
      expect(params).toEqual({status: InboxDocumentStatus.Ready, search: 'invoice'});
    });

    it('keeps status 0 (Preparing) and sends null for no filter, so ApiBaseService drops it', () => {
      service.list(InboxDocumentStatus.Preparing, '').subscribe();
      expect(apiBaseServiceSpy.get.mock.lastCall[1]).toEqual({status: 0, search: null});

      service.list(null, '').subscribe();
      expect(apiBaseServiceSpy.get.mock.lastCall[1]).toEqual({status: null, search: null});
    });

    it('hands a DocumentDate suggestion\'s targetDate on as the DateInterceptor\'s Date; its UTC day is the date', () => {
      // "2026-10-03T00:00:00" from the server, read by the host's DateInterceptor as midnight UTC.
      const suggestion: InboxSuggestionModel = {
        id: 1, kind: InboxSuggestionKind.DocumentDate, targetId: 0, targetName: '', source: 0, confidence: 0.9,
        evidence: null, page: 1, reason: null, targetDate: new Date(Date.UTC(2026, 9, 3)),
      };
      apiBaseServiceSpy.get.mockReturnValue(of({success: true, model: [{id: 5, suggestions: [suggestion]}]}));

      let received: InboxSuggestionModel | undefined;
      service.list(null, '').subscribe(result => received = result.model[0].suggestions[0]);

      expect(serverDay(received.targetDate)).toBe('2026-10-03');
    });
  });

  it('getPdf GETs the PDF as a blob', () => {
    service.getPdf(7).subscribe();
    expect(apiBaseServiceSpy.getBlobData).toHaveBeenCalledWith('api/backend-configuration-pn/inbox/7/file');
  });

  it('file POSTs name, properties, tags, folder and the document date', () => {
    const body = {
      name: 'Invoice 42', propertyIds: [3], tagIds: [5, 6],
      folderId: 9, documentDate: '2026-10-03', documentDateApproved: false,
    };
    service.file(7, body).subscribe();
    expect(apiBaseServiceSpy.post).toHaveBeenCalledWith('api/backend-configuration-pn/inbox/7/file', body);
  });

  it('undo POSTs to {id}/undo with an empty body', () => {
    service.undo(7).subscribe();
    expect(apiBaseServiceSpy.post).toHaveBeenCalledWith('api/backend-configuration-pn/inbox/7/undo', {});
  });

  it('reject carries the block flag in the query string, false by default', () => {
    service.reject(7).subscribe();
    expect(apiBaseServiceSpy.post).toHaveBeenLastCalledWith('api/backend-configuration-pn/inbox/7/reject?block=false', {});

    service.reject(7, true).subscribe();
    expect(apiBaseServiceSpy.post).toHaveBeenLastCalledWith('api/backend-configuration-pn/inbox/7/reject?block=true', {});
  });

  it('getSettings GETs the settings route without a toast (the page shows failures inline)', () => {
    service.getSettings().subscribe();
    expect(apiBaseServiceSpy.getNoToast).toHaveBeenCalledWith('api/backend-configuration-pn/inbox/settings');
    expect(apiBaseServiceSpy.get).not.toHaveBeenCalled();
    expect(BackendConfigurationPnInboxMethods.Settings).toBe('api/backend-configuration-pn/inbox/settings');
  });

  it('updateSettings PUTs the full list of blocked senders', () => {
    const body = {
      senderRules: [
        {pattern: '@example.org', kind: InboxSenderRuleKind.Block},
        {pattern: 'spam@example.com', kind: InboxSenderRuleKind.Block},
      ],
    };
    service.updateSettings(body).subscribe();
    expect(apiBaseServiceSpy.put).toHaveBeenCalledWith('api/backend-configuration-pn/inbox/settings', body);
  });

  it('rotateAddress POSTs to settings/rotate-address', () => {
    service.rotateAddress().subscribe();
    expect(apiBaseServiceSpy.post).toHaveBeenCalledWith('api/backend-configuration-pn/inbox/settings/rotate-address', {});
  });

  it('routes every call through the account-disabled handler', () => {
    service.list(null, '').subscribe();
    service.getPdf(7).subscribe();
    service.file(7, {name: 'Invoice 42', propertyIds: [3], tagIds: []}).subscribe();
    service.undo(7).subscribe();
    service.reject(7).subscribe();
    service.getSettings().subscribe();
    service.updateSettings({senderRules: []}).subscribe();
    service.rotateAddress().subscribe();
    expect(handle).toHaveBeenCalledTimes(8);
  });
});
