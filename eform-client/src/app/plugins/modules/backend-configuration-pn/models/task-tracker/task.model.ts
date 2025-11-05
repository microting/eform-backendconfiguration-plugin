import {SharedTagModel} from 'src/app/common/models';
import {RepeatTypeEnum} from '../../enums';

export interface TaskModel {
  property: string;
  taskName: string;
  tags: SharedTagModel[];
  workerNames: string[];
  workerIds: number[];
  startTask: Date;
  repeatEvery: number;
  repeatType: RepeatTypeEnum;
  deadlineTask: Date,
  nextExecutionTime: string;
  taskIsExpired: boolean;
  sdkCaseId: number;
  templateId: number;
  propertyId: number;
  complianceId: number;
  areaId: number;
  areaRuleId: number;
  areaRulePlanId: number;
  weeks: WeekListModel[];
  sdkFolderName: string;
  createdInWizard: boolean;
  movedToExpiredFolder: boolean
}

export interface WeekListModel {
  weekNumber: number;
  weekRange: number;
  dateList: DateListModel[];
}

export interface DateListModel {
  date: Date;
  isTask: boolean;
}
