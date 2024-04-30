import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import {OperationDataResult, OperationResult} from 'src/app/common/models';
import { ApiBaseService } from 'src/app/common/services';
import {
  WorkOrderCaseCreateModel,
  WorkOrderCaseForReadModel,
  WorkOrderCaseModel, WorkOrderCaseUpdateModel,
} from '../models';
import {
  TaskManagementFiltrationModel
} from '../state/task-management/task-management.reducer';
import {
  TaskManagementRequestModel
} from 'src/app/plugins/modules/backend-configuration-pn/models/task-management/task-management-request.model';

export let BackendConfigurationPnTaskManagementMethods = {
  Index: 'api/backend-configuration-pn/task-management/index',
  WorkOrderCases: 'api/backend-configuration-pn/task-management/',
  EntityItemsList: 'api/backend-configuration-pn/task-management/entity-items',
  WordReport: 'api/backend-configuration-pn/task-management/word',
  ExcelReport: 'api/backend-configuration-pn/task-management/excel',
};

@Injectable({
  providedIn: 'root',
})
  export class BackendConfigurationPnTaskManagementService {
  constructor(private apiBaseService: ApiBaseService) {}

  getWorkOrderCases(model: TaskManagementRequestModel): Observable<OperationDataResult<WorkOrderCaseModel[]>> {
    return this.apiBaseService.post(BackendConfigurationPnTaskManagementMethods.Index, model);
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

  updateWorkOrderCase(workOrderCase: WorkOrderCaseUpdateModel): Observable<OperationResult> {
    return this.apiBaseService.putFormData(
      BackendConfigurationPnTaskManagementMethods.WorkOrderCases,
      workOrderCase
    );
  }

  downloadWordReport(model: TaskManagementFiltrationModel): Observable<any> {
    return this.apiBaseService.getBlobData(BackendConfigurationPnTaskManagementMethods.WordReport, model);
  }

  downloadExcelReport(model: TaskManagementFiltrationModel): Observable<any> {
    return this.apiBaseService.getBlobData(BackendConfigurationPnTaskManagementMethods.ExcelReport, model);
  }
}
