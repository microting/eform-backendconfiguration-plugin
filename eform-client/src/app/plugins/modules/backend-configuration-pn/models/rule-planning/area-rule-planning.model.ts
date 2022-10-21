import {AreaRuleT2AlarmsEnum, AreaRuleT2TypesEnum} from 'src/app/plugins/modules/backend-configuration-pn/enums';
import {EntityItemModel} from 'src/app/common/models';

export class AreaRulePlanningModel {
  ruleId: number;
  status: boolean;
  sendNotifications: boolean;
  startDate: string;
  assignedSites: AreaRuleAssignedSitesModel[] = [];
  propertyId: number;
  typeSpecificFields: TypeSpecificFieldsAreaRulePlanning;
  complianceEnabled: boolean;
  entityItemsListForCreate: Array<EntityItemModel> = [];
}

export class AreaRuleAssignedSitesModel {
  siteId: number;
  checked: boolean;
}

export class TypeSpecificFieldsAreaRulePlanning {
  repeatEvery?: number;
  repeatType?: number;
  dayOfMonth?: number;
  dayOfWeek?: number;
  type?: AreaRuleT2TypesEnum;
  alarm?: AreaRuleT2AlarmsEnum;
  startDate?: string;
  endDate?: string;
  hoursAndEnergyEnabled?: boolean;
  complianceEnabled?: boolean;
  complianceModifiable?: boolean;
  notifications?: boolean;
  notificationsModifiable?: boolean;
}
