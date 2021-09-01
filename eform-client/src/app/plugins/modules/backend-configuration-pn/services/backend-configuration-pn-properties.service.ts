import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import {
  OperationDataResult,
  OperationResult,
  Paged,
} from 'src/app/common/models';
import {
  PropertyCreateModel,
  PropertyModel,
  PropertiesRequestModel,
  PropertyUpdateModel,
} from '../models/properties';
import { ApiBaseService } from 'src/app/common/services';

export let BackendConfigurationPnPropertiesMethods = {
  Properties: 'api/items-planning-pn/properties',
  PropertiesIndex: 'api/items-planning-pn/properties/index',
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

  getSingleProperty(
    planningId: number
  ): Observable<OperationDataResult<PropertyModel>> {
    return this.apiBaseService.get(
      BackendConfigurationPnPropertiesMethods.Properties,
      {
        id: planningId,
      }
    );
  }

  updateProperty(model: PropertyUpdateModel): Observable<OperationResult> {
    return this.apiBaseService.put(
      BackendConfigurationPnPropertiesMethods.Properties,
      model
    );
  }

  createProperty(model: PropertyCreateModel): Observable<OperationResult> {
    return this.apiBaseService.post(
      BackendConfigurationPnPropertiesMethods.Properties,
      model
    );
  }

  deleteProperty(propertyId: number): Observable<OperationResult> {
    return this.apiBaseService.delete(
      BackendConfigurationPnPropertiesMethods.Properties,
      { id: propertyId }
    );
  }
}
