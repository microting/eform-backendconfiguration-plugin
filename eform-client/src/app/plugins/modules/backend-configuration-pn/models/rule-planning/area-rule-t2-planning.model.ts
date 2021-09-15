import {
  AreaRuleT2AlarmsEnum,
  AreaRuleT2TypesEnum,
} from '../../enums';
import { AreaRulePlanningModel } from './area-rule-planning.model';

export class AreaRuleT2PlanningModel extends AreaRulePlanningModel {
  type: AreaRuleT2TypesEnum;
  alarm: AreaRuleT2AlarmsEnum;
  repeatEvery: number;
  repeatType: number;
}

