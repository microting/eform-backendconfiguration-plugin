import {Injectable} from '@angular/core';
import {ApiBaseService} from 'src/app/common/services';
import {TaskWizardFiltrationModel} from '../modules/task-wizard/components/store';
import {Observable} from 'rxjs';
import {OperationDataResult, OperationResult, Paged} from 'src/app/common/models';

export let BackendConfigurationPnTaskWizardMethods = {
  TaskWizard: 'api/backend-configuration-pn/task-wizard',
  Index: 'api/backend-configuration-pn/task-wizard/index',
};

@Injectable({
  providedIn: 'root',
})
export class BackendConfigurationPnTaskWizardService {
  constructor(private apiBaseService: ApiBaseService) {
  }

  getTasks(model: TaskWizardFiltrationModel): Observable<OperationDataResult<Paged<any>>> { // todo change any to normal class
    return this.apiBaseService.post(BackendConfigurationPnTaskWizardMethods.Index, model);
  }

  getTaskById(id: number): Observable<OperationDataResult<any>> { // todo change any to normal class
    return this.apiBaseService.get(`${BackendConfigurationPnTaskWizardMethods.TaskWizard}/${id}`);
  }

  createTask(createModel: any): Observable<OperationResult> { // todo change any to normal class
    return this.apiBaseService.post(BackendConfigurationPnTaskWizardMethods.TaskWizard, createModel);
  }

  updateTask(updateModel: any): Observable<OperationResult> { // todo change any to normal class
    return this.apiBaseService.put(BackendConfigurationPnTaskWizardMethods.Index, updateModel);
  }

  deleteTaskById(id: number): Observable<OperationResult> { // todo change any to normal class
    return this.apiBaseService.delete(`${BackendConfigurationPnTaskWizardMethods.TaskWizard}/${id}`);
  }
}
