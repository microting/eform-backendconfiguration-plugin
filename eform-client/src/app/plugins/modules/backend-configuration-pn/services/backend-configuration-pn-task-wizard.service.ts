import {Injectable} from '@angular/core';
import {ApiBaseService} from 'src/app/common/services';
import {Observable} from 'rxjs';
import {CommonDictionaryModel, OperationDataResult, OperationResult} from 'src/app/common/models';
import {TaskWizardCreateModel, TaskWizardEditModel, TaskWizardModel, TaskWizardRequestModel, TaskWizardTaskModel} from '../models';

export let BackendConfigurationPnTaskWizardMethods = {
  TaskWizard: 'api/backend-configuration-pn/task-wizard',
  Index: 'api/backend-configuration-pn/task-wizard/index',
  Properties: 'api/backend-configuration-pn/task-wizard/properties',
  Deactivate: 'api/backend-configuration-pn/task-wizard/deactivate',
};

@Injectable({
  providedIn: 'root',
})
export class BackendConfigurationPnTaskWizardService {
  deactivateMultipleTasks(selectedPlanningsCheckboxes: number[]): Observable<OperationResult> {
    return this.apiBaseService.put(BackendConfigurationPnTaskWizardMethods.Deactivate, selectedPlanningsCheckboxes);
  }
  constructor(private apiBaseService: ApiBaseService) {
  }

  getTasks(model: TaskWizardRequestModel): Observable<OperationDataResult<TaskWizardModel[]>> {
    return this.apiBaseService.post(BackendConfigurationPnTaskWizardMethods.Index, model);
  }

  getTaskById(id: number, compliance: boolean): Observable<OperationDataResult<TaskWizardTaskModel>> {
    return this.apiBaseService.get(`${BackendConfigurationPnTaskWizardMethods.TaskWizard}/${id}?compliance=${compliance}`);
  }

  createTask(createModel: TaskWizardCreateModel): Observable<OperationResult> {
    return this.apiBaseService.post(BackendConfigurationPnTaskWizardMethods.TaskWizard, createModel);
  }

  updateTask(updateModel: TaskWizardEditModel): Observable<OperationResult> {
    return this.apiBaseService.put(BackendConfigurationPnTaskWizardMethods.TaskWizard, updateModel);
  }

  deleteTaskById(id: number): Observable<OperationResult> {
    return this.apiBaseService.delete(`${BackendConfigurationPnTaskWizardMethods.TaskWizard}/${id}`);
  }

  getAllPropertiesDictionary(fullNames: boolean = false): Observable<
    OperationDataResult<CommonDictionaryModel[]>
  > {
    return this.apiBaseService.get(
      BackendConfigurationPnTaskWizardMethods.Properties,
      {fullNames: false}
    );
  }
}
