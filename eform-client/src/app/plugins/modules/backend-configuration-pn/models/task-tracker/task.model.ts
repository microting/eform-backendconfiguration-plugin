import {SharedTagModel} from 'src/app/common/models';

export interface TaskModel {
  property: string,
  taskName: string,
  tags: SharedTagModel[],
  workers: string[],
  startTask: Date,
  repeat: string,
  deadlineTask: Date,
}
