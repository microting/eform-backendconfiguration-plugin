import {SharedTagModel} from 'src/app/common/models';

export interface TaskModel {
  propertyName: string,
  taskName: string,
  tags: SharedTagModel[],
  workers: string,
  startTask: Date,
  repeatTypeTask: string,
  deadlineTask: Date,
}
