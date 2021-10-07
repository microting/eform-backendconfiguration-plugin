import {
  CommonDictionaryModel,
  CommonTranslationModel,
} from 'src/app/common/models';
import {
  AreaRuleT1Model,
  AreaRuleT2Model,
  AreaRuleT3Model,
  AreaRuleT5Model,
} from './';
import { AreaRulePlanningModel } from '../rule-planning/area-rule-planning.model';

export class AreaRuleSimpleModel {
  id: number;
  eformName: string;
  translatedName: string;
  isDefault: boolean;
  planningStatus: boolean;
  typeSpecificFields:
    | AreaRuleT1Model
    | AreaRuleT2Model
    | AreaRuleT3Model
    | AreaRuleT5Model;
  planningId?: number;
  initialFields?: AreaRuleInitialFieldsModel;
}

export class AreaRuleModel {
  id: number;
  eformName: string;
  eformId: number;
  isDefault: boolean;
  translatedNames: CommonDictionaryModel[] = [];
  typeSpecificFields:
    | AreaRuleT1Model
    | AreaRuleT2Model
    | AreaRuleT3Model
    | AreaRuleT5Model;
  planning: AreaRulePlanningModel;
}

export class AreaRuleInitialFieldsModel {
  eformId?: number;
  eformName?: string;
  sendNotifications?: boolean;
  repeatEvery?: number;
  repeatType?: number;
  dayOfWeek?: number;
  type?: number;
  alarm?: number;
  endDate?: string;
}
