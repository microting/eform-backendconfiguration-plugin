import {Injectable} from '@angular/core';
import {Observable} from 'rxjs';
import {OperationDataResult, OperationResult} from 'src/app/common/models';
import {ApiBaseService} from 'src/app/common/services';
import {
  WorkOrderCaseCreateModel,
  TaskModel
} from '../models';
import {TaskTrackerFiltrationModel} from '../modules/task-tracker/components/store';

export let BackendConfigurationPnTaskTrackerMethods = {
  TaskTracker: 'api/backend-configuration-pn/task-tracker',
  Index: 'api/backend-configuration-pn/task-tracker/index',
};

@Injectable({
  providedIn: 'root',
})
export class BackendConfigurationPnTaskTrackerService {
  constructor(private apiBaseService: ApiBaseService) {
  }

  getTasks(model: TaskTrackerFiltrationModel): Observable<OperationDataResult<TaskModel[]>> {
    return this.apiBaseService.post(BackendConfigurationPnTaskTrackerMethods.Index, model);
  }

  createTask(task: WorkOrderCaseCreateModel): Observable<OperationResult> {
    return this.apiBaseService.post(
      BackendConfigurationPnTaskTrackerMethods.TaskTracker,
      task
    );
  }
}
