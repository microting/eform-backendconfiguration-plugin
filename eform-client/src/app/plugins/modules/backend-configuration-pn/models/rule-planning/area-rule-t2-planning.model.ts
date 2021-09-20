import { AreaRuleT2AlarmsEnum, AreaRuleT2TypesEnum } from '../../enums';

export class AreaRuleT2PlanningModel {
  type: AreaRuleT2TypesEnum;
  alarm: AreaRuleT2AlarmsEnum;
  repeatEvery: number;
  repeatType: number;
  startDate: string;
}

