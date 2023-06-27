import {AreaRuleT2AlarmsEnum, AreaRuleT2TypesEnum} from '../../enums';
import {EntityItemModel} from 'src/app/common/models';

export class AreaRulePlanningModel {
  ruleId: number;
  status: boolean;
  serverStatus: boolean;
  sendNotifications: boolean;
  startDate: string | Date;
  useStartDateAsStartOfPeriod: boolean;
  assignedSites: AreaRuleAssignedSitesModel[] = [];
  propertyId: number;
  typeSpecificFields: TypeSpecificFieldsAreaRulePlanning;
  complianceEnabled: boolean;
  entityItemsListForCreate: Array<EntityItemModel> = [];
}

export class AreaRuleAssignedSitesModel {
  siteId: number;
  checked: boolean;
  status: number;
}

export class TypeSpecificFieldsAreaRulePlanning {
  repeatEvery?: number;
  repeatType?: number;
  dayOfMonth?: number;
  dayOfWeek?: number;
  type?: AreaRuleT2TypesEnum;
  alarm?: AreaRuleT2AlarmsEnum;
  startDate?: string | Date;
  endDate?: string;
  hoursAndEnergyEnabled?: boolean;
  complianceEnabled?: boolean;
  complianceModifiable?: boolean;
  notifications?: boolean;
  notificationsModifiable?: boolean;
}
