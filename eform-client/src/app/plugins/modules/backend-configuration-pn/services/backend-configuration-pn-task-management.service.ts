import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import {OperationDataResult} from 'src/app/common/models';
import { ApiBaseService } from 'src/app/common/services';
import {
  WorkOrderCaseForReadModel,
  WorkOrderCaseModel,
} from '../models';
import {TaskManagementFiltrationModel} from '../modules/task-management/components/store';

export let BackendConfigurationPnTaskManagementMethods = {
  WorkOrderCases: 'api/backend-configuration-pn/task-management',
};

@Injectable({
  providedIn: 'root',
})
  export class BackendConfigurationPnTaskManagementService {
  constructor(private apiBaseService: ApiBaseService) {}

  getWorkOrderCases(model: TaskManagementFiltrationModel): Observable<OperationDataResult<WorkOrderCaseModel[]>> {
    return this.apiBaseService.get(BackendConfigurationPnTaskManagementMethods.WorkOrderCases, model);
  }

  getWorkOrderCase(workOrderCaseId: number): Observable<OperationDataResult<WorkOrderCaseForReadModel>> {
    return this.apiBaseService.get(
      `${BackendConfigurationPnTaskManagementMethods.WorkOrderCases}/${workOrderCaseId}`
    );
  }
}
