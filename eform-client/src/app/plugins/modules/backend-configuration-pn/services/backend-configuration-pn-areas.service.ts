import {Injectable} from '@angular/core';
import {Observable} from 'rxjs';
import {CommonPaginationState, OperationDataResult, OperationResult, Paged} from 'src/app/common/models';
import {ApiBaseService} from 'src/app/common/services';
import {
  AreaRuleSimpleModel,
  AreaRulePlanningModel,
  AreaRulesCreateModel,
  AreaRuleModel,
  AreaRuleUpdateModel,
  AreaModel, TaskWorkerModel,
} from '../models';

export let BackendConfigurationPnAreasMethods = {
  Area: 'api/backend-configuration-pn/area',
  AreaByRuleId: 'api/backend-configuration-pn/area-by-rule-id',
  AreaRules: 'api/backend-configuration-pn/area-rules',
  AreaRulesMultipleDelete: 'api/backend-configuration-pn/area-rules/multiple-delete',
  AreaRulesForType7: 'api/backend-configuration-pn/area-rules/type-7',
  AreaRulesForType8: 'api/backend-configuration-pn/area-rules/type-8',
  AreaRulesIndex: 'api/backend-configuration-pn/area-rules/index',
  AreaRulesIndexByPropertyIdAndAreaId: 'api/backend-configuration-pn/area-rules/index-by-propertyId-and-areaId',
  AreaRulePlanning: 'api/backend-configuration-pn/area-rules/planning',
  AreaRulePlanningById: 'api/backend-configuration-pn/area-rules/planning-by-id',
  WorkerPlannings: 'api/backend-configuration-pn/area-rules/worker-plannings',
};

@Injectable({
  providedIn: 'root',
})
export class BackendConfigurationPnAreasService {
  constructor(private apiBaseService: ApiBaseService) {
  }

  getAreaByPropertyAreaId(propertyAreaId: number): Observable<OperationDataResult<AreaModel>> {
    return this.apiBaseService.get(BackendConfigurationPnAreasMethods.Area, {
      propertyAreaId,
    });
  }

  getAreaRules(model: { propertyAreaId: number, sort: string, isSortDsc: boolean, }
  ): Observable<OperationDataResult<AreaRuleSimpleModel[]>> {
    return this.apiBaseService.get(
      BackendConfigurationPnAreasMethods.AreaRulesIndex,
      {propertyAreaId: model.propertyAreaId, sort: model.sort, isSortDsc: model.isSortDsc}
    );
  }

  getAreaRulesByPropertyIdAndAreaId(propertyId: number, areaId: number): Observable<OperationDataResult<AreaRuleSimpleModel[]>> {
    return this.apiBaseService.get(
      BackendConfigurationPnAreasMethods.AreaRulesIndexByPropertyIdAndAreaId,
      {propertyId, areaId}
    );
  }

  getAreaRulePlanningByRuleId(
    ruleId: number
  ): Observable<OperationDataResult<AreaRulePlanningModel>> {
    return this.apiBaseService.get(
      BackendConfigurationPnAreasMethods.AreaRulePlanning,
      {ruleId}
    );
  }

  getSingleAreaRule(
    ruleId: number,
    propertyId: number
  ): Observable<OperationDataResult<AreaRuleModel>> {
    return this.apiBaseService.get(
      BackendConfigurationPnAreasMethods.AreaRules,
      {ruleId, propertyId}
    );
  }

  updateAreaRule(model: AreaRuleUpdateModel): Observable<OperationResult> {
    return this.apiBaseService.put(
      BackendConfigurationPnAreasMethods.AreaRules,
      model
    );
  }

  updateAreaRulePlanning(
    model: AreaRulePlanningModel
  ): Observable<OperationResult> {
    return this.apiBaseService.put(
      BackendConfigurationPnAreasMethods.AreaRulePlanning,
      model
    );
  }

  createAreaRules(model: AreaRulesCreateModel): Observable<OperationResult> {
    return this.apiBaseService.post(
      BackendConfigurationPnAreasMethods.AreaRules,
      model
    );
  }

  deleteAreaRule(areaId: number): Observable<OperationResult> {
    return this.apiBaseService.delete(
      BackendConfigurationPnAreasMethods.AreaRules,
      {areaId}
    );
  }

  deleteAreaRules(areaRuleIds: number[]): Observable<OperationResult> {
    return this.apiBaseService.post(
      BackendConfigurationPnAreasMethods.AreaRulesMultipleDelete,
      areaRuleIds
    );
  }

  getAreaRulesForType7(): Observable<OperationDataResult<{ folderName: string, areaRuleNames: string[] }[]>> {
    return this.apiBaseService.get(
      BackendConfigurationPnAreasMethods.AreaRulesForType7
    );
  }

  getAreaRulesForType8(): Observable<OperationDataResult<{ folderName: string, areaRuleNames: string[] }[]>> {
    return this.apiBaseService.get(
      BackendConfigurationPnAreasMethods.AreaRulesForType8
    );
  }

  getAreaRulePlanningByPlanningId(
    planningId: number
  ): Observable<OperationDataResult<AreaRulePlanningModel>> {
    return this.apiBaseService.get(
      BackendConfigurationPnAreasMethods.AreaRulePlanningById,
      {planningId}
    );
  }

  getTaskWorkerAssignments(siteId: number, pagination: CommonPaginationState): Observable<OperationDataResult<Paged<TaskWorkerModel>>> {
    return this.apiBaseService.get(
      BackendConfigurationPnAreasMethods.WorkerPlannings,
      {siteId: siteId, ...pagination}
    );
  }

  getAreaByRuleId(areaRuleId: number): Observable<OperationDataResult<AreaModel>> {
    return this.apiBaseService.get(BackendConfigurationPnAreasMethods.AreaByRuleId, {
      areaRuleId,
    });
  }
}
