import { CommonSimpleTranslationModel } from 'src/app/common/models';
import { AreaRuleT1Model } from './area-rule-t1.model';
import { AreaRuleT2Model } from './area-rule-t2.model';
import { AreaRuleT3Model } from './area-rule-t3.model';

export class AreaRulesCreateModel {
  areaRules: AreaRuleCreateModel[] = [];
}

export class AreaRuleCreateModel {
  translatedNames: CommonSimpleTranslationModel[] = [];
  typeSpecificFields: AreaRuleT1Model | AreaRuleT2Model | AreaRuleT3Model;
}
