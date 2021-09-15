import {
  AreaRuleT2AlarmsEnum,
  AreaRuleT2TypesEnum,
} from 'src/app/plugins/modules/backend-configuration-pn/enums';
import { AreaRuleCreateModel } from 'src/app/plugins/modules/backend-configuration-pn/models/rules/area-rule-create.model';

export class AreaRuleT2CreateModel extends AreaRuleCreateModel {
  type: AreaRuleT2TypesEnum;
  alarm: AreaRuleT2AlarmsEnum;
}
