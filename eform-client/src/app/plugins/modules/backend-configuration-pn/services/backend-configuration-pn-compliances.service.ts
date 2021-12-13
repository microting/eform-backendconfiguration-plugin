import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { OperationDataResult, Paged } from 'src/app/common/models';
import { CompliancesModel, CompliancesRequestModel } from '../models';
import { ApiBaseService } from 'src/app/common/services';

export let BackendConfigurationPnCompliancesMethods = {
  Compliances: 'api/backend-configuration-pn/compliances/index',
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
}
