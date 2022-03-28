import {
  AreaRuleT2AlarmsEnum,
  AreaRuleT2TypesEnum,
} from 'src/app/plugins/modules/backend-configuration-pn/enums';

export class AreaRuleTypeSpecificFields {
  eformId?: number;
  eformName?: string;
  type?: AreaRuleT2TypesEnum;
  alarm?: AreaRuleT2AlarmsEnum;
  checklistStable?: boolean;
  tailBite?: boolean;
  dayOfWeek?: number;
  dayOfWeekName?: string;
  repeatEvery?: number;
  groupId?: number;
}
