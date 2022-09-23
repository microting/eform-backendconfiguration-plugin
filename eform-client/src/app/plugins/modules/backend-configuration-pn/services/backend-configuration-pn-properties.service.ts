import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import {
  AdvEntitySelectableItemModel,
  CommonDictionaryModel, DeviceUserRequestModel,
  OperationDataResult,
  OperationResult,
  Paged, SiteDto,
} from 'src/app/common/models';
import {
  PropertyCreateModel,
  PropertyModel,
  PropertiesRequestModel,
  PropertyUpdateModel,
  PropertyAreaModel,
  PropertyAreasUpdateModel,
  PropertyAssignWorkersModel, ResultModel,
} from '../models';
import { ApiBaseService } from 'src/app/common/services';
import {DeviceUserModel} from 'src/app/plugins/modules/backend-configuration-pn/models/device-users';
import {ChrResultModel} from 'src/app/plugins/modules/backend-configuration-pn/models/properties/chr-result.model';

export let BackendConfigurationPnPropertiesMethods = {
  Properties: 'api/backend-configuration-pn/properties',
  PropertyAreas: 'api/backend-configuration-pn/property-areas',
  PropertiesAssignment: 'api/backend-configuration-pn/properties/assignment',
  PropertiesIndex: 'api/backend-configuration-pn/properties/index',
  UpdateDeviceUser: 'api/backend-configuration-pn/properties/assignment/update-device-user',
  CreateEntityList: 'api/backend-configuration-pn/property-areas/create-entity-list/',
  CreateDeviceUser: 'api/backend-configuration-pn/properties/assignment/create-device-user',
  GetAll: 'api/backend-configuration-pn/properties/assignment/index-device-user',
  GetCompanyType: 'api/backend-configuration-pn/properties/get-company-type',
  GetChrInformation: 'api/backend-configuration-pn/properties/get-chr-information',
}

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

  readProperty(id: number): Observable<OperationDataResult<PropertyModel>> {
    return this.apiBaseService.get(
      BackendConfigurationPnPropertiesMethods.Properties,
      { id: id }
    );
  }

  getChrInformation(id: number): Observable<OperationDataResult<ChrResultModel>> {
    return this.apiBaseService.get(
      BackendConfigurationPnPropertiesMethods.GetChrInformation,
      { cvrNumber: id }
    );
  }

  getCompanyType(id: number): Observable<OperationDataResult<ResultModel>> {
    return this.apiBaseService.get(
      BackendConfigurationPnPropertiesMethods.GetCompanyType,
      { cvrNumber: id }
    );
  }

  createSingleDeviceUser(
    model: DeviceUserModel
  ): Observable<OperationDataResult<number>> {
    return this.apiBaseService.put<DeviceUserModel>(
      BackendConfigurationPnPropertiesMethods.CreateDeviceUser,
      model
    );
  }

  updateSingleDeviceUser(model: DeviceUserModel): Observable<OperationResult> {
    return this.apiBaseService.post<DeviceUserModel>(
      BackendConfigurationPnPropertiesMethods.UpdateDeviceUser,
      model
    );
  }

  getDeviceUsersFiltered(
    model: DeviceUserRequestModel
  ): Observable<OperationDataResult<Array<DeviceUserModel>>> {
    return this.apiBaseService.post<Array<DeviceUserModel>>(
      BackendConfigurationPnPropertiesMethods.GetAll,
      model
    );
  }

  createEntityList(model: Array<AdvEntitySelectableItemModel>, propertyAreaId: number): Observable<OperationResult> {
    return this.apiBaseService.post(
      BackendConfigurationPnPropertiesMethods.CreateEntityList + propertyAreaId,
      model
    );
  }
}
