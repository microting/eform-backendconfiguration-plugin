import {
  AreaRuleT2AlarmsEnum,
  AreaRuleT2TypesEnum,
} from 'src/app/plugins/modules/backend-configuration-pn/enums';
import {PoolHoursModel} from 'src/app/plugins/modules/backend-configuration-pn/models/pools/pool-hour.model';

export class AreaRuleTypeSpecificFields {
  eformId?: number;
  eformName?: string;
  type?: AreaRuleT2TypesEnum;
  alarm?: AreaRuleT2AlarmsEnum;
  dayOfWeek?: number;
  dayOfWeekName?: string;
  repeatEvery?: number;
  repeatType?: number;
  groupId?: number;
  complianceEnabled?: boolean;
  complianceModifiable?: boolean;
  notifications?: boolean;
  notificationsModifiable?: boolean;
  poolHoursModel?: PoolHoursModel;
}
