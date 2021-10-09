import { AreaRuleT1PlanningModel } from './area-rule-t1-planning.model';
import { AreaRuleT2PlanningModel } from './area-rule-t2-planning.model';
import { AreaRuleT4PlanningModel } from './area-rule-t4-planning.model';
import { AreaRuleT5PlanningModel } from './area-rule-t5-planning.model';
import { AreaRuleT6PlanningModel } from './area-rule-t6-planning.model';

export class AreaRulePlanningModel {
  ruleId: number;
  status: boolean;
  sendNotifications: boolean;
  startDate: string;
  assignedSites: AreaRuleAssignedSitesModel[] = [];
  propertyId: number;
  typeSpecificFields:
    | AreaRuleT1PlanningModel
    | AreaRuleT2PlanningModel
    | AreaRuleT4PlanningModel
    | AreaRuleT5PlanningModel
    | AreaRuleT6PlanningModel;
}

export class AreaRuleAssignedSitesModel {
  siteId: number;
  checked: boolean;
}
