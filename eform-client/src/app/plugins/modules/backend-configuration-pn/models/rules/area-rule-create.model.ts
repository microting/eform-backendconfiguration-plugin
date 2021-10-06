import { CommonDictionaryModel } from 'src/app/common/models';
import {
  AreaRuleT1Model,
  AreaRuleT2Model,
  AreaRuleT3Model,
  AreaRuleT5Model,
} from 'src/app/plugins/modules/backend-configuration-pn/models';

export class AreaRulesCreateModel {
  propertyAreaId: number;
  areaRules: AreaRuleCreateModel[] = [];
}

export class AreaRuleCreateModel {
  translatedNames: CommonDictionaryModel[] = [];
  typeSpecificFields:
    | AreaRuleT1Model
    | AreaRuleT2Model
    | AreaRuleT3Model
    | AreaRuleT5Model;
}
