import { CommonDictionaryModel } from 'src/app/common/models';
import { AreaRuleT5Model } from './area-rule-t5.model';
import { AreaRuleT1Model } from './area-rule-t1.model';
import { AreaRuleT2Model } from './area-rule-t2.model';
import { AreaRuleT3Model } from './area-rule-t3.model';

export class AreaRulesCreateModel {
  areaId: number;
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
