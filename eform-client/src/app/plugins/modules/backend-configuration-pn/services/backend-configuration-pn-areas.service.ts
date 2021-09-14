// import { Injectable } from '@angular/core';
// import { Observable } from 'rxjs';
// import {
//   CommonDictionaryModel,
//   OperationDataResult,
//   OperationResult,
//   Paged,
// } from 'src/app/common/models';
// import {
//   PropertyAssignmentWorkerModel,
//   PropertyAssignWorkersModel,
// } from '../models/properties/property-workers-assignment.model';
// import {
//   PropertyCreateModel,
//   PropertyModel,
//   PropertiesRequestModel,
//   PropertyUpdateModel,
// } from '../models/properties';
// import { ApiBaseService } from 'src/app/common/services';
//
// export let BackendConfigurationPnPropertiesMethods = {
//   Properties: 'api/backend-configuration-pn/areas',
//   PropertiesAssignment: 'api/backend-configuration-pn/properties/assignment',
//   PropertiesIndex: 'api/backend-configuration-pn/properties/index',
// };
//
// @Injectable({
//   providedIn: 'root',
// })
// export class BackendConfigurationPnPropertiesService {
//   constructor(private apiBaseService: ApiBaseService) {}
//
//   getAllProperties(
//     model: PropertiesRequestModel
//   ): Observable<OperationDataResult<Paged<PropertyModel>>> {
//     return this.apiBaseService.post(
//       BackendConfigurationPnPropertiesMethods.PropertiesIndex,
//       model
//     );
//   }
//
//   getAllPropertiesDictionary(): Observable<
//     OperationDataResult<CommonDictionaryModel[]>
//     > {
//     return this.apiBaseService.get(
//       `${BackendConfigurationPnPropertiesMethods.PropertiesIndex}/dictionary`
//     );
//   }
//
//   updateProperty(model: PropertyUpdateModel): Observable<OperationResult> {
//     return this.apiBaseService.put(
//       BackendConfigurationPnPropertiesMethods.Properties,
//       model
//     );
//   }
//
//   createProperty(model: PropertyCreateModel): Observable<OperationResult> {
//     return this.apiBaseService.post(
//       BackendConfigurationPnPropertiesMethods.Properties,
//       model
//     );
//   }
//
//   assignPropertiesToWorker(
//     model: PropertyAssignWorkersModel
//   ): Observable<OperationResult> {
//     return this.apiBaseService.post(
//       BackendConfigurationPnPropertiesMethods.PropertiesAssignment,
//       model
//     );
//   }
//
//   getPropertiesAssignments(): Observable<
//     OperationDataResult<PropertyAssignWorkersModel[]>
//     > {
//     return this.apiBaseService.get(
//       `${BackendConfigurationPnPropertiesMethods.PropertiesAssignment}`
//     );
//   }
//
//   removeWorkerAssignments(deviceUserId: number): Observable<OperationResult> {
//     return this.apiBaseService.delete(
//       `${BackendConfigurationPnPropertiesMethods.PropertiesAssignment}/${deviceUserId}`
//     );
//   }
//
//   deleteProperty(propertyId: number): Observable<OperationResult> {
//     return this.apiBaseService.delete(
//       BackendConfigurationPnPropertiesMethods.Properties,
//       { id: propertyId }
//     );
//   }
// }
