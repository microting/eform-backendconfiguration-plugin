import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import {OperationDataResult, OperationResult, Paged, ReplyElementDto, ReplyRequest} from 'src/app/common/models';
import { CompliancesModel, CompliancesRequestModel } from '../models';
import { ApiBaseService } from 'src/app/common/services';

export let BackendConfigurationPnCompliancesMethods = {
  Compliances: 'api/backend-configuration-pn/compliances/index',
  ComplianceStatus: 'api/backend-configuration-pn/compliances/compliance',
  GetCases: 'api/backend-configuration-pn/compliances/cases',
};

@Injectable({
  providedIn: 'root',
})
export class BackendConfigurationPnCompliancesService {
  constructor(private apiBaseService: ApiBaseService) {}

  getAllCompliances(
    model: CompliancesRequestModel
  ): Observable<OperationDataResult<Paged<CompliancesModel>>> {
    return this.apiBaseService.post(
      BackendConfigurationPnCompliancesMethods.Compliances,
      model
    );
  }

  getComplianceStatus(
    propertyId: number
  ): Observable<OperationDataResult<number>> {
    return this.apiBaseService.get(
      BackendConfigurationPnCompliancesMethods.ComplianceStatus + '?propertyId=' + propertyId
    );
  }

  getCase(
    id: number,
    templateId: number
  ): Observable<OperationDataResult<ReplyElementDto>> {
    return this.apiBaseService.get<ReplyElementDto>(BackendConfigurationPnCompliancesMethods.GetCases, {
      id: id,
      templateId: templateId,
    });
  }

  updateCase(
    model: ReplyRequest,
    templateId: number
  ): Observable<OperationResult> {
    return this.apiBaseService.put<ReplyRequest>(
      BackendConfigurationPnCompliancesMethods.GetCases,
      model
    );
  }
}
