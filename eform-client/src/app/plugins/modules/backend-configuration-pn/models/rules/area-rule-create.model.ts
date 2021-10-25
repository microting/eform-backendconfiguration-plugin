import { CommonDictionaryModel } from 'src/app/common/models';
import { AreaRuleTypeSpecificFields } from '../';

export class AreaRulesCreateModel {
  propertyAreaId: number;
  areaRules: AreaRuleCreateModel[] = [];
}

export class AreaRuleCreateModel {
  translatedNames: CommonDictionaryModel[] = [];
  typeSpecificFields: AreaRuleTypeSpecificFields;
}
