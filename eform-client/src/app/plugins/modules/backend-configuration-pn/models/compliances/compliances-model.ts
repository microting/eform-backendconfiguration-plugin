import { PropertyCompliancesColorBadgesEnum } from '../../enums';

export class CompliancesModel {
  id: number;
  controlArea: string;
  itemName: string;
  deadline: Date;
  responsibles: string[];
  compliance: PropertyCompliancesColorBadgesEnum;
  planningId: number;
  eformId: number;
  caseId: number;

  get linkToCaseEdit(): string {
    return `/cases/edit/${this.caseId}/${this.eformId}`
  }

  get linkToPlanning(): string {
    return `/plugins/items-planning-pn/plannings/edit/${this.planningId}`
  }
}
