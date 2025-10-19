import {CommonTranslationsModel} from 'src/app/common/models';
import {RepeatTypeEnum, TaskWizardStatusesEnum} from '../../enums';

export interface TaskWizardCreateModel {
  propertyId: number,
  folderId: number,
  itemPlanningTagId?: number,
  tagIds: number[],
  translates: CommonTranslationsModel[],
  eformId: number,
  startDate: Date,
  repeatType: RepeatTypeEnum,
  repeatEvery: number,
  status: TaskWizardStatusesEnum,
  sites: number[],
  complianceEnabled: boolean,
}
