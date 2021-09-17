import {
  CommonDictionaryModel,
  CommonTranslationModel,
} from 'src/app/common/models';
import { AreaRuleT1Model, AreaRuleT2Model, AreaRuleT3Model } from './';
import { AreaRulePlanningModel } from '../rule-planning/area-rule-planning.model';

export class AreaRuleSimpleModel {
  id: number;
  eformName: string;
  translatedName: string;
  isDefault: boolean;
  planningStatus: boolean;
}

export class AreaRuleModel {
  id: number;
  eformName: string;
  eformId: number;
  isDefault: boolean;
  languages: string[];
  translatedNames: CommonDictionaryModel[] = [];
  typeSpecificFields: AreaRuleT1Model | AreaRuleT2Model | AreaRuleT3Model;
  planning: AreaRulePlanningModel;
}
