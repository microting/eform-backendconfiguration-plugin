import { AreaRulePlanningModel } from '../rule-planning/area-rule-planning.model';

export class AreaRuleModel {
  id: number;
  eformName: string;
  languages: string[];
  planning: AreaRulePlanningModel;
}
