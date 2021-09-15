import { CommonTranslationModel } from 'src/app/common/models';
import { AreaRuleT2Model } from './area-rule-t2.model';
import { AreaRuleT3Model } from './area-rule-t3.model';
import { AreaRuleT1Model } from './area-rule-t1.model';

export class AreaRuleUpdateModel {
  id: number;
  translatedNames: CommonTranslationModel[] = [];
  type: 1 | 2 | 3 | 4 | 5;
  typeSpecificFields: AreaRuleT1Model | AreaRuleT2Model | AreaRuleT3Model;
}
