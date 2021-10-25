import { CommonDictionaryModel } from 'src/app/common/models';
import { AreaRuleTypeSpecificFields } from './';
import { AreaRulePlanningModel } from '../rule-planning/area-rule-planning.model';

export class AreaRuleSimpleModel {
  id: number;
  eformName: string;
  translatedName: string;
  isDefault: boolean;
  planningStatus: boolean;
  typeSpecificFields: AreaRuleTypeSpecificFields;
  planningId?: number;
  initialFields?: AreaRuleInitialFieldsModel;
}

export class AreaRuleModel {
  id: number;
  eformName: string;
  eformId: number;
  isDefault: boolean;
  translatedNames: CommonDictionaryModel[] = [];
  typeSpecificFields: AreaRuleTypeSpecificFields;
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
