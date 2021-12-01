import { PropertyCompliancesColorBadgesEnum } from 'src/app/plugins/modules/backend-configuration-pn/enums';

export class CompliancesModel {
  id: number;
  controlArea: string;
  itemName: string;
  deadline: Date;
  responsibles: string[];
  compliance: PropertyCompliancesColorBadgesEnum;
}
