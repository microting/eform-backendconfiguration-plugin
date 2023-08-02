import {RepeatTypeEnum, TaskWizardStatusesEnum} from '../../enums';
import {CommonTranslationsModel} from 'src/app/common/models';

export interface TaskWizardTaskModel {
  id: number;
  propertyId: number;
  folderId: number;
  itemPlanningTagId: number,
  tags: number[];
  translations: CommonTranslationsModel[];
  eformId: number;
  eformName: string;
  startDate: Date;
  repeatType: RepeatTypeEnum;
  repeatEvery: number;
  status: TaskWizardStatusesEnum;
  assignedTo: number[];
}
