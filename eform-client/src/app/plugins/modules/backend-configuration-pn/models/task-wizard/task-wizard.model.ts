import {SharedTagModel} from 'src/app/common/models';
import {RepeatTypeEnum, TaskWizardStatusesEnum} from '../../enums';

export interface TaskWizardModel {
  id: number,
  property: string,
  folder: string,
  tags: SharedTagModel[],
  taskName: string,
  eform: string,
  eformId: number,
  startDate: Date,
  repeatType: RepeatTypeEnum,
  repeatEvery: number,
  status: TaskWizardStatusesEnum,
  assignedTo: string[],
  createdInGuide: boolean;
  createdAt: Date;
  updatedAt: Date;
  planningId: number;
}
