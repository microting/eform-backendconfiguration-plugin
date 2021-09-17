import { CommonTranslationModel } from 'src/app/common/models';
import { AreaRuleT5Model } from './area-rule-t5.model';
import { AreaRuleT2Model } from './area-rule-t2.model';
import { AreaRuleT3Model } from './area-rule-t3.model';
import { AreaRuleT1Model } from './area-rule-t1.model';

export class AreaRuleUpdateModel {
  id: number;
  eformId: number;
  eformName: string;
  languages: string[];
  translatedNames: CommonTranslationModel[] = [];
  typeSpecificFields:
    | AreaRuleT1Model
    | AreaRuleT2Model
    | AreaRuleT3Model
    | AreaRuleT5Model;
}
