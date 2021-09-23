import {
  AreaRuleT2AlarmsEnum,
  AreaRuleT2TypesEnum,
} from 'src/app/plugins/modules/backend-configuration-pn/enums';

export class AreaRuleT2Model {
  type: AreaRuleT2TypesEnum;
  alarm: AreaRuleT2AlarmsEnum;
}
