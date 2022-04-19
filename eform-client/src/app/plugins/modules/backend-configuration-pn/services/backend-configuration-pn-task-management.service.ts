import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import {OperationDataResult, OperationResult} from 'src/app/common/models';
import { ApiBaseService } from 'src/app/common/services';
import {
  WorkOrderCaseCreateModel,
  WorkOrderCaseForReadModel,
  WorkOrderCaseModel,
} from '../models';
import {TaskManagementFiltrationModel} from '../modules/task-management/components/store';

export let BackendConfigurationPnTaskManagementMethods = {
  WorkOrderCases: 'api/backend-configuration-pn/task-management',
  EntityItemsList: 'api/backend-configuration-pn/task-management/entity-items',
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

  getEntityItemsListByPropertyId(propertyId: number): Observable<OperationDataResult<string[]>> {
    return this.apiBaseService.get(
      BackendConfigurationPnTaskManagementMethods.EntityItemsList,
      { propertyId: propertyId }
    );
  }

  deleteWorkOrderCase(workOrderCaseId: number): Observable<OperationResult> {
    return this.apiBaseService.delete(
      BackendConfigurationPnTaskManagementMethods.WorkOrderCases,
      { workOrderCaseId: workOrderCaseId }
    );
  }

  createWorkOrderCase(workOrderCase: WorkOrderCaseCreateModel): Observable<OperationResult> {
    return this.apiBaseService.postFormData(
      BackendConfigurationPnTaskManagementMethods.WorkOrderCases,
      workOrderCase
    );
  }
}
