import { AreaRuleCreateModel } from 'src/app/plugins/modules/backend-configuration-pn/models/rules/area-rule-create.model';

export class AreaRuleT3CreateModel extends AreaRuleCreateModel {
  eformId: number;
  checklistStable: boolean;
  tailBite: boolean;
}
