import { CommonDictionaryModel } from 'src/app/common/models';
import { AreaRuleTypeSpecificFields } from './';
import { AreaRulePlanningModel } from '../rule-planning/area-rule-planning.model';

export class AreaRuleNameAndTypeSpecificFields {
  translatedName: string;
  typeSpecificFields: AreaRuleTypeSpecificFields;
}

export class AreaRuleSimpleModel extends AreaRuleNameAndTypeSpecificFields {
  id: number;
  eformName: string;
  isDefault: boolean;
  planningStatus: boolean;
  planningId?: number;
  initialFields?: AreaRuleInitialFieldsModel;
  secondaryeFormId?: number;
  secondaryeFormName?: string;
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
  complianceEnabled: boolean;
}
