import { CommonTranslationModel } from 'src/app/common/models';

export class AreaRulesCreateModel {
  areaRules: AreaRuleCreateModel[] = [];
}

export class AreaRuleCreateModel {
  translatedNames: CommonTranslationModel[] = [];
}

