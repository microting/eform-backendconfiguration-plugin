import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import {
  CommonDictionaryModel,
  OperationDataResult,
  OperationResult,
  Paged,
} from 'src/app/common/models';
import {
  PropertyAssignmentWorkerModel,
  PropertyAssignWorkersModel,
} from '../models/properties/property-workers-assignment.model';
import {
  PropertyCreateModel,
  PropertyModel,
  PropertiesRequestModel,
  PropertyUpdateModel,
  PropertyAreaModel,
  PropertyAreasUpdateModel,
} from '../models/properties';
import { ApiBaseService } from 'src/app/common/services';

export let BackendConfigurationPnPropertiesMethods = {
  Properties: 'api/backend-configuration-pn/properties',
  PropertyAreas: 'api/backend-configuration-pn/property-areas',
  PropertiesAssignment: 'api/backend-configuration-pn/properties/assignment',
  PropertiesIndex: 'api/backend-configuration-pn/properties/index',
};

@Injectable({
  providedIn: 'root',
})
export class BackendConfigurationPnPropertiesService {
  constructor(private apiBaseService: ApiBaseService) {}

  getAllProperties(
    model: PropertiesRequestModel
  ): Observable<OperationDataResult<Paged<PropertyModel>>> {
    return this.apiBaseService.post(
      BackendConfigurationPnPropertiesMethods.PropertiesIndex,
      model
    );
  }

  getPropertyAreas(
    propertyId: number
  ): Observable<OperationDataResult<PropertyAreaModel[]>> {
    return this.apiBaseService.get(
      `${BackendConfigurationPnPropertiesMethods.PropertyAreas}`,
      { propertyId: propertyId }
    );
  }

  getAllPropertiesDictionary(): Observable<
    OperationDataResult<CommonDictionaryModel[]>
  > {
    return this.apiBaseService.get(
      `${BackendConfigurationPnPropertiesMethods.Properties}/dictionary`
    );
  }

  createProperty(model: PropertyCreateModel): Observable<OperationResult> {
    return this.apiBaseService.post(
      BackendConfigurationPnPropertiesMethods.Properties,
      model
    );
  }

  updateProperty(model: PropertyUpdateModel): Observable<OperationResult> {
    return this.apiBaseService.put(
      BackendConfigurationPnPropertiesMethods.Properties,
      model
    );
  }

  updatePropertyAreas(
    model: PropertyAreasUpdateModel
  ): Observable<OperationResult> {
    return this.apiBaseService.put(
      BackendConfigurationPnPropertiesMethods.PropertyAreas,
      model
    );
  }

  assignPropertiesToWorker(
    model: PropertyAssignWorkersModel
  ): Observable<OperationResult> {
    return this.apiBaseService.post(
      BackendConfigurationPnPropertiesMethods.PropertiesAssignment,
      model
    );
  }

  updateAssignPropertiesToWorker(
    model: PropertyAssignWorkersModel
  ): Observable<OperationResult> {
    return this.apiBaseService.put(
      BackendConfigurationPnPropertiesMethods.PropertiesAssignment,
      model
    );
  }

  getPropertiesAssignments(): Observable<
    OperationDataResult<PropertyAssignWorkersModel[]>
  > {
    return this.apiBaseService.get(
      `${BackendConfigurationPnPropertiesMethods.PropertiesAssignment}`
    );
  }

  removeWorkerAssignments(deviceUserId: number): Observable<OperationResult> {
    return this.apiBaseService.delete(
      `${BackendConfigurationPnPropertiesMethods.PropertiesAssignment}`,
      { deviceUserId: deviceUserId }
    );
  }

  deleteProperty(propertyId: number): Observable<OperationResult> {
    return this.apiBaseService.delete(
      BackendConfigurationPnPropertiesMethods.Properties,
      { propertyId: propertyId }
    );
  }
}
