import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { OperationDataResult, OperationResult } from 'src/app/common/models';
import { ApiBaseService } from 'src/app/common/services';
import {
  AreaRuleSimpleModel,
  AreaRulePlanningModel,
  AreaRulesCreateModel,
  AreaRuleModel,
  AreaRuleUpdateModel,
  AreaModel,
} from '../models';

export let BackendConfigurationPnAreasMethods = {
  Area: 'api/backend-configuration-pn/area',
  AreaRules: 'api/backend-configuration-pn/area-rules',
  AreaRulesIndex: 'api/backend-configuration-pn/area-rules/index',
  AreaRulePlanning: 'api/backend-configuration-pn/area-rules/planning',
};

@Injectable({
  providedIn: 'root',
})
export class BackendConfigurationPnAreasService {
  constructor(private apiBaseService: ApiBaseService) {}

  getArea(propertyAreaId: number): Observable<OperationDataResult<AreaModel>> {
    return this.apiBaseService.get(BackendConfigurationPnAreasMethods.Area, {
      propertyAreaId,
    });
  }

  getAreaRules(
    propertyAreaId: number
  ): Observable<OperationDataResult<AreaRuleSimpleModel[]>> {
    return this.apiBaseService.get(
      BackendConfigurationPnAreasMethods.AreaRulesIndex,
      { propertyAreaId }
    );
  }

  getAreaRulePlanning(
    ruleId: number
  ): Observable<OperationDataResult<AreaRulePlanningModel>> {
    return this.apiBaseService.get(
      BackendConfigurationPnAreasMethods.AreaRulePlanning,
      { ruleId }
    );
  }

  getSingleAreaRule(
    ruleId: number,
    propertyId: number
  ): Observable<OperationDataResult<AreaRuleModel>> {
    return this.apiBaseService.get(
      BackendConfigurationPnAreasMethods.AreaRules,
      { ruleId, propertyId }
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
      { areaId }
    );
  }
}
