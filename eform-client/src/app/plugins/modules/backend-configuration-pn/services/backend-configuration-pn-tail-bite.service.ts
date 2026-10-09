import {Injectable} from '@angular/core';
import {Observable} from 'rxjs';
import {OperationDataResult, OperationResult} from 'src/app/common/models';
import {ApiBaseService} from 'src/app/common/services';
import {
  TailBiteActionType,
  TailBiteAssignableWorker,
  TailBiteLocationTree,
  TailBiteOccupancy,
  TailBiteOutbreakDetail,
  TailBiteOutbreakRegistrations,
  TailBiteOutbreakSummary,
  TailBitePropertyStatus,
  TailBiteRule,
  TailBiteRuleInput,
  TailBiteRulePreview,
  TailBiteRuleVersion,
  TailBiteSaveAssessmentRequest,
  TailBiteWorker,
} from '../models';

export const BackendConfigurationPnTailBiteMethods = {
  Base: 'api/backend-configuration-pn/tail-bite',
};

/**
 * Tail biting (halebid) web admin: TailBiteSetupController, TailBiteOutbreaksController and
 * TailBiteWebController in the plugin. A failed result carries the server's message
 * ("Not found or no access." for a missing or foreign id); the toasting calls show it.
 */
@Injectable({providedIn: 'root'})
export class BackendConfigurationPnTailBiteService {
  constructor(private apiBaseService: ApiBaseService) {}

  private url(path: string): string {
    return `${BackendConfigurationPnTailBiteMethods.Base}/${path}`;
  }

  // ---------- properties, workers, managers ----------

  /** Every property with its enabled flag (plugin-admin list, for the managers dialog). */
  getProperties(): Observable<OperationDataResult<TailBitePropertyStatus[]>> {
    return this.apiBaseService.get(this.url('properties'));
  }

  /** The enabled properties the caller is a worker on: the ones the tail-bite area can open for them. */
  getMyProperties(): Observable<OperationDataResult<TailBitePropertyStatus[]>> {
    return this.apiBaseService.get(this.url('my-properties'));
  }

  /**
   * The workers the outbreak page can make responsible: for a tail-bite manager of the property, names only. The
   * outbreak page must use this, not getWorkers (the managers dialog's list needs the worker-update permission).
   */
  getAssignableWorkers(propertyId: number): Observable<OperationDataResult<TailBiteAssignableWorker[]>> {
    return this.apiBaseService.get(this.url(`properties/${propertyId}/assignable-workers`));
  }

  /** Every worker with PropertyWorker ids and manager flags: the managers dialog's list (needs DeviceUsers.Update). */
  getWorkers(propertyId: number): Observable<OperationDataResult<TailBiteWorker[]>> {
    return this.apiBaseService.get(this.url(`properties/${propertyId}/workers`));
  }

  enable(propertyId: number): Observable<OperationResult> {
    return this.apiBaseService.post(this.url(`properties/${propertyId}/enable`), {});
  }

  setManager(propertyWorkerId: number, isManager: boolean): Observable<OperationResult> {
    return this.apiBaseService.put(this.url(`property-workers/${propertyWorkerId}/manager`), {isManager});
  }

  // ---------- locations and occupancy ----------

  getTree(propertyId: number): Observable<OperationDataResult<TailBiteLocationTree>> {
    return this.apiBaseService.get(this.url(`properties/${propertyId}/tree`));
  }

  createLocation(parentId: number, name: string): Observable<OperationDataResult<number>> {
    return this.apiBaseService.post(this.url('locations'), {parentId, name});
  }

  createPenRange(parentId: number, prefix: string, from: number, to: number): Observable<OperationDataResult<number[]>> {
    return this.apiBaseService.post(this.url('locations/range'), {parentId, prefix, from, to});
  }

  renameLocation(id: number, name: string): Observable<OperationResult> {
    return this.apiBaseService.put(this.url(`locations/${id}`), {name});
  }

  moveLocation(id: number, newParentId: number): Observable<OperationResult> {
    return this.apiBaseService.put(this.url(`locations/${id}/move`), {newParentId});
  }

  deleteLocation(id: number): Observable<OperationResult> {
    return this.apiBaseService.delete(this.url(`locations/${id}`));
  }

  getOccupancy(propertyId: number): Observable<OperationDataResult<TailBiteOccupancy[]>> {
    return this.apiBaseService.get(this.url(`properties/${propertyId}/occupancy`));
  }

  /** validFromUtc: an ISO instant, normally midnight UTC of the picked day (toUtcMidnightIso). */
  setOccupancy(locationId: number, pigCount: number, validFromUtc: string): Observable<OperationResult> {
    return this.apiBaseService.put(this.url(`locations/${locationId}/occupancy`), {pigCount, validFromUtc});
  }

  // ---------- action types ----------

  getActionTypes(propertyId: number): Observable<OperationDataResult<TailBiteActionType[]>> {
    return this.apiBaseService.get(this.url(`properties/${propertyId}/action-types`));
  }

  createActionType(propertyId: number, name: string): Observable<OperationDataResult<number>> {
    return this.apiBaseService.post(this.url(`properties/${propertyId}/action-types`), {name});
  }

  renameActionType(id: number, name: string): Observable<OperationResult> {
    return this.apiBaseService.put(this.url(`action-types/${id}`), {name});
  }

  deleteActionType(id: number): Observable<OperationResult> {
    return this.apiBaseService.delete(this.url(`action-types/${id}`));
  }

  // ---------- rules ----------

  getRules(propertyId: number): Observable<OperationDataResult<TailBiteRule[]>> {
    return this.apiBaseService.get(this.url(`properties/${propertyId}/rules`));
  }

  getRuleHistory(ruleId: number): Observable<OperationDataResult<TailBiteRuleVersion[]>> {
    return this.apiBaseService.get(this.url(`rules/${ruleId}/history`));
  }

  createRule(input: TailBiteRuleInput): Observable<OperationDataResult<number>> {
    return this.apiBaseService.post(this.url('rules'), input);
  }

  updateRule(id: number, input: TailBiteRuleInput): Observable<OperationResult> {
    return this.apiBaseService.put(this.url(`rules/${id}`), input);
  }

  deleteRule(id: number): Observable<OperationResult> {
    return this.apiBaseService.delete(this.url(`rules/${id}`));
  }

  /**
   * The 90-day dry run. `postNoToast` on purpose: it fires on every (debounced) edit of the
   * rule form and a failure is shown inline next to the form, not as a toast per keystroke.
   */
  previewRule(input: TailBiteRuleInput): Observable<OperationDataResult<TailBiteRulePreview>> {
    return this.apiBaseService.postNoToast(this.url('rules/preview'), input);
  }

  // ---------- outbreaks ----------

  getOutbreaks(propertyId: number, openOnly: boolean): Observable<OperationDataResult<TailBiteOutbreakSummary[]>> {
    return this.apiBaseService.get(this.url(`properties/${propertyId}/outbreaks`), {openOnly});
  }

  getOutbreak(id: number): Observable<OperationDataResult<TailBiteOutbreakDetail>> {
    return this.apiBaseService.get(this.url(`outbreaks/${id}`));
  }

  getOutbreakRegistrations(id: number): Observable<OperationDataResult<TailBiteOutbreakRegistrations>> {
    return this.apiBaseService.get(this.url(`outbreaks/${id}/registrations`));
  }

  saveAssessment(outbreakId: number, request: TailBiteSaveAssessmentRequest): Observable<OperationResult> {
    return this.apiBaseService.put(this.url(`outbreaks/${outbreakId}/assessment`), request);
  }

  setActionDone(actionId: number, done: boolean): Observable<OperationResult> {
    return this.apiBaseService.put(this.url(`actions/${actionId}/done`), {done});
  }

  withdrawAction(actionId: number, reason: string): Observable<OperationResult> {
    return this.apiBaseService.put(this.url(`actions/${actionId}/withdraw`), {reason});
  }

  reassignAction(actionId: number, responsibleSiteId: number): Observable<OperationResult> {
    return this.apiBaseService.put(this.url(`actions/${actionId}/reassign`), {responsibleSiteId});
  }

  closeOutbreak(outbreakId: number): Observable<OperationResult> {
    return this.apiBaseService.put(this.url(`outbreaks/${outbreakId}/close`), {});
  }

  cancelRegistration(registrationId: number, reason: string): Observable<OperationResult> {
    return this.apiBaseService.put(this.url(`registrations/${registrationId}/cancel`), {reason});
  }
}
