import { CommonDictionaryModel } from 'src/app/common/models';
import { PropertyCompliancesColorBadgesEnum } from '../../enums';

export class PropertyModel {
  id: number;
  name: string;
  chr: string;
  cvr: string;
  address: string;
  languages: CommonDictionaryModel[] = [];
  isWorkersAssigned: boolean;
  complianceStatus: PropertyCompliancesColorBadgesEnum;
  complianceStatusThirty: PropertyCompliancesColorBadgesEnum;
  complianceStatusBadge: string;
  workorderEnable: boolean;
}
