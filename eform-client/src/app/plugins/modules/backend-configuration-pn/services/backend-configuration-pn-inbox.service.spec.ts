import {of} from 'rxjs';
import {
  BackendConfigurationPnInboxMethods,
  BackendConfigurationPnInboxService,
} from './backend-configuration-pn-inbox.service';
import {InboxDocumentStatus, InboxSenderRuleKind} from '../models';

/**
 * Pins the wire contract of the Indbakke service against InboxController: URL, verb and body of each
 * call. What happens on the server is covered by the C# integration tests; nothing here can see it.
 */
describe('BackendConfigurationPnInboxService', () => {
  let service: BackendConfigurationPnInboxService;
  let apiBaseServiceSpy: any;

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
    service = new BackendConfigurationPnInboxService(apiBaseServiceSpy);
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
  });

  it('getPdf GETs the PDF as a blob', () => {
    service.getPdf(7).subscribe();
    expect(apiBaseServiceSpy.getBlobData).toHaveBeenCalledWith('api/backend-configuration-pn/inbox/7/file');
  });

  it('file POSTs name, propertyIds and tagIds', () => {
    const body = {name: 'Invoice 42', propertyIds: [3], tagIds: [5, 6]};
    service.file(7, body).subscribe();
    expect(apiBaseServiceSpy.post).toHaveBeenCalledWith('api/backend-configuration-pn/inbox/7/file', body);
  });

  it.each([
    ['undo', 'undo'],
    ['reject', 'reject'],
    ['approveSender', 'approve-sender'],
  ])('%s POSTs to {id}/%s with an empty body', (method, segment) => {
    (service as any)[method](7).subscribe();
    expect(apiBaseServiceSpy.post).toHaveBeenCalledWith(`api/backend-configuration-pn/inbox/7/${segment}`, {});
  });

  it('rejectSender carries the block flag in the query string', () => {
    service.rejectSender(7, true).subscribe();
    expect(apiBaseServiceSpy.post).toHaveBeenLastCalledWith('api/backend-configuration-pn/inbox/7/reject-sender?block=true', {});

    service.rejectSender(7, false).subscribe();
    expect(apiBaseServiceSpy.post).toHaveBeenLastCalledWith('api/backend-configuration-pn/inbox/7/reject-sender?block=false', {});
  });

  it('getSettings GETs the settings route without a toast (the page shows failures inline)', () => {
    service.getSettings().subscribe();
    expect(apiBaseServiceSpy.getNoToast).toHaveBeenCalledWith('api/backend-configuration-pn/inbox/settings');
    expect(apiBaseServiceSpy.get).not.toHaveBeenCalled();
    expect(BackendConfigurationPnInboxMethods.Settings).toBe('api/backend-configuration-pn/inbox/settings');
  });

  it('updateSettings PUTs the policy and the full rule list', () => {
    const body = {
      unknownSenderPolicy: 'refuse' as const,
      senderRules: [
        {pattern: '@example.org', kind: InboxSenderRuleKind.Allow},
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
});
