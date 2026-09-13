import {of} from 'rxjs';
import {
  BackendConfigurationWorkerTagsMethods,
  BackendConfigurationPnWorkerTagsService,
} from './backend-configuration-pn-worker-tags.service';

/**
 * Pins the ROUTE of the teams list (#1213).
 *
 * The whole point of the fix is that the calendar's Teams list no longer comes from
 * the core `/api/tags/index` (all SDK tags, eForm template tags included) but from the
 * plugin endpoint that filters to tags with at least one live worker member. The
 * filtering itself is a server-side SQL predicate and is covered by
 * `WorkerTagsListTests` on the backend — nothing here can see it.
 *
 * What this proves: the frontend asks the plugin route, with no client-side filtering
 * of the response (the model comes back untouched).
 * What it does not prove: that `CalendarContainerComponent.loadTeams()` calls THIS
 * service. That component has no spec, and adding one would mean standing up its
 * ~10 collaborators.
 */
describe('BackendConfigurationPnWorkerTagsService', () => {
  let service: BackendConfigurationPnWorkerTagsService;
  let apiBaseServiceSpy: any;

  beforeEach(() => {
    apiBaseServiceSpy = {get: jest.fn()};
    apiBaseServiceSpy.get.mockReturnValue(of({success: true, model: []}));
    service = new BackendConfigurationPnWorkerTagsService(apiBaseServiceSpy);
  });

  it('reads the plugin worker-tags endpoint, not the core tags index', () => {
    service.getWorkerTags().subscribe();

    expect(apiBaseServiceSpy.get).toHaveBeenCalledTimes(1);
    const [url] = apiBaseServiceSpy.get.mock.lastCall;
    expect(url).toBe('api/backend-configuration-pn/worker-tags');
    expect(url).toBe(BackendConfigurationWorkerTagsMethods.WorkerTags);
    expect(url).not.toContain('api/tags');
  });

  it('passes the server list through unchanged (no client-side filtering)', (done) => {
    const model = [{id: 3, name: 'Team A'}, {id: 9, name: 'Team B'}];
    apiBaseServiceSpy.get.mockReturnValue(of({success: true, model}));

    service.getWorkerTags().subscribe(res => {
      expect(res.model).toEqual(model);
      done();
    });
  });
});
