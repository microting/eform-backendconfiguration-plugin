import {of} from 'rxjs';
import {TailBiteFactor} from '../models';
import {BackendConfigurationPnTailBiteService} from './backend-configuration-pn-tail-bite.service';

/**
 * Pins every route, verb and body of the tail-bite web service against the plugin's
 * controllers (TailBiteSetupController, TailBiteOutbreaksController, TailBiteWebController).
 */
describe('BackendConfigurationPnTailBiteService', () => {
  const base = 'api/backend-configuration-pn/tail-bite';
  let api: {get: jest.Mock; post: jest.Mock; postNoToast: jest.Mock; put: jest.Mock; delete: jest.Mock};
  let service: BackendConfigurationPnTailBiteService;

  beforeEach(() => {
    const ok = () => jest.fn().mockReturnValue(of({success: true}));
    api = {get: ok(), post: ok(), postNoToast: ok(), put: ok(), delete: ok()};
    service = new BackendConfigurationPnTailBiteService(api as any);
  });

  it.each([
    ['getProperties', () => service.getProperties(), `${base}/properties`],
    ['getMyProperties', () => service.getMyProperties(), `${base}/my-properties`],
    ['getWorkers', () => service.getWorkers(3), `${base}/properties/3/workers`],
    ['getAssignableWorkers', () => service.getAssignableWorkers(3), `${base}/properties/3/assignable-workers`],
    ['getTree', () => service.getTree(3), `${base}/properties/3/tree`],
    ['getOccupancy', () => service.getOccupancy(3), `${base}/properties/3/occupancy`],
    ['getActionTypes', () => service.getActionTypes(3), `${base}/properties/3/action-types`],
    ['getRules', () => service.getRules(3), `${base}/properties/3/rules`],
    ['getRuleHistory', () => service.getRuleHistory(9), `${base}/rules/9/history`],
    ['getOutbreak', () => service.getOutbreak(5), `${base}/outbreaks/5`],
    ['getOutbreakRegistrations', () => service.getOutbreakRegistrations(5), `${base}/outbreaks/5/registrations`],
  ])('%s GETs the right route', (_name, call, url) => {
    (call as () => any)().subscribe();
    expect(api.get).toHaveBeenCalledWith(url);
  });

  it('getOutbreaks passes openOnly as a query parameter', () => {
    service.getOutbreaks(3, false).subscribe();
    expect(api.get).toHaveBeenCalledWith(`${base}/properties/3/outbreaks`, {openOnly: false});
  });

  it.each([
    ['enable', () => service.enable(3), `${base}/properties/3/enable`, {}],
    ['createLocation', () => service.createLocation(1, 'Stald A'), `${base}/locations`, {parentId: 1, name: 'Stald A'}],
    ['createPenRange', () => service.createPenRange(4, 'Sti', 301, 312), `${base}/locations/range`,
      {parentId: 4, prefix: 'Sti', from: 301, to: 312}],
    ['createActionType', () => service.createActionType(3, 'Halm'), `${base}/properties/3/action-types`, {name: 'Halm'}],
    ['createRule', () => service.createRule({locationId: 2, minBittenPigs: 5, minSevere: null, windowDays: 7, countDepth: 1}),
      `${base}/rules`, {locationId: 2, minBittenPigs: 5, minSevere: null, windowDays: 7, countDepth: 1}],
  ])('%s POSTs the body', (_name, call, url, body) => {
    (call as () => any)().subscribe();
    expect(api.post).toHaveBeenCalledWith(url, body);
  });

  it.each([
    ['setManager', () => service.setManager(8, true), `${base}/property-workers/8/manager`, {isManager: true}],
    ['renameLocation', () => service.renameLocation(4, 'Sektion 4'), `${base}/locations/4`, {name: 'Sektion 4'}],
    ['moveLocation', () => service.moveLocation(4, 2), `${base}/locations/4/move`, {newParentId: 2}],
    ['setOccupancy', () => service.setOccupancy(4, 360, '2026-09-15T00:00:00Z'), `${base}/locations/4/occupancy`,
      {pigCount: 360, validFromUtc: '2026-09-15T00:00:00Z'}],
    ['renameActionType', () => service.renameActionType(6, 'Reb'), `${base}/action-types/6`, {name: 'Reb'}],
    ['updateRule', () => service.updateRule(9, {locationId: 2, minBittenPigs: null, minSevere: 1, windowDays: 7, countDepth: 2}),
      `${base}/rules/9`, {locationId: 2, minBittenPigs: null, minSevere: 1, windowDays: 7, countDepth: 2}],
    ['saveAssessment', () => service.saveAssessment(5, {
      answers: {water: false, feed: true, activityMaterial: false, climate: false, health: false, management: false},
      newActions: [{factor: TailBiteFactor.Feed, description: 'Tjek foderautomat', responsibleSiteId: 7, followUpDate: '2026-10-06'}],
    }), `${base}/outbreaks/5/assessment`, {
      answers: {water: false, feed: true, activityMaterial: false, climate: false, health: false, management: false},
      newActions: [{factor: 1, description: 'Tjek foderautomat', responsibleSiteId: 7, followUpDate: '2026-10-06'}],
    }],
    ['setActionDone', () => service.setActionDone(11, true), `${base}/actions/11/done`, {done: true}],
    ['withdrawAction', () => service.withdrawAction(11, 'Ikke relevant'), `${base}/actions/11/withdraw`, {reason: 'Ikke relevant'}],
    ['reassignAction', () => service.reassignAction(11, 8), `${base}/actions/11/reassign`, {responsibleSiteId: 8}],
    ['closeOutbreak', () => service.closeOutbreak(5), `${base}/outbreaks/5/close`, {}],
    ['cancelRegistration', () => service.cancelRegistration(21, 'Forkert sti'), `${base}/registrations/21/cancel`,
      {reason: 'Forkert sti'}],
  ])('%s PUTs the body', (_name, call, url, body) => {
    (call as () => any)().subscribe();
    expect(api.put).toHaveBeenCalledWith(url, body);
  });

  it.each([
    ['deleteLocation', () => service.deleteLocation(4), `${base}/locations/4`],
    ['deleteActionType', () => service.deleteActionType(6), `${base}/action-types/6`],
    ['deleteRule', () => service.deleteRule(9), `${base}/rules/9`],
  ])('%s DELETEs the route', (_name, call, url) => {
    (call as () => any)().subscribe();
    expect(api.delete).toHaveBeenCalledWith(url);
  });

  it('previewRule posts without a toast', () => {
    const input = {locationId: 2, minBittenPigs: 5, minSevere: 1, windowDays: 7, countDepth: 1};
    service.previewRule(input).subscribe();
    expect(api.postNoToast).toHaveBeenCalledWith(`${base}/rules/preview`, input);
    expect(api.post).not.toHaveBeenCalled();
  });
});
