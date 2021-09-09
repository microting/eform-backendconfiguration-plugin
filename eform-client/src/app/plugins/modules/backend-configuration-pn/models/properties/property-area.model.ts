import { PropertyAreaPlanningStatusesEnum } from '../../enums';

export class PropertyAreaModel {
  id: number;
  name: string;
  description: string;
  activated: boolean;
  status: PropertyAreaPlanningStatusesEnum;
}
