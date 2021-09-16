import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { OperationDataResult, OperationResult } from 'src/app/common/models';
import { ApiBaseService } from 'src/app/common/services';
import {
  AreaRuleSimpleModel,
  AreaRulePlanningModel,
  AreaRulesCreateModel,
  AreaRuleModel,
} from '../models';

export let BackendConfigurationPnAreasMethods = {
  AreaRules: 'api/backend-configuration-pn/area-rules',
  AreaRulesIndex: 'api/backend-configuration-pn/area-rules/index',
  AreaRulePlanning: 'api/backend-configuration-pn/area-rules/planning',
};

@Injectable({
  providedIn: 'root',
})
export class BackendConfigurationPnAreasService {
  constructor(private apiBaseService: ApiBaseService) {}

  getAreaRules(
    areaId: number
  ): Observable<OperationDataResult<AreaRuleSimpleModel[]>> {
    return this.apiBaseService.get(
      BackendConfigurationPnAreasMethods.AreaRulesIndex,
      { areaId }
    );
  }

  getSingleAreaRule(
    ruleId: number
  ): Observable<OperationDataResult<AreaRuleModel>> {
    return this.apiBaseService.get(
      BackendConfigurationPnAreasMethods.AreaRules,
      { ruleId }
    );
  }

  updateAreaRule(model: any): Observable<OperationResult> {
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
