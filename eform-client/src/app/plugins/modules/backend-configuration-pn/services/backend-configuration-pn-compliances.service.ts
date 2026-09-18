import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import {OperationDataResult, OperationResult, Paged, ReplyElementDto, ReplyRequest} from 'src/app/common/models';
import { ComplianceModel, CompliancesRequestModel } from '../models';
import { ApiBaseService } from 'src/app/common/services';

export let BackendConfigurationPnCompliancesMethods = {
  Compliances: 'api/backend-configuration-pn/compliances/index',
  ComplianceStatus: 'api/backend-configuration-pn/compliances/compliance',
  GetCases: 'api/backend-configuration-pn/compliances/cases',
  UpdateCaseFromCalendar: 'api/backend-configuration-pn/compliances/cases/calendar',
  DeleteCompliance: 'api/backend-configuration-pn/compliances/delete',
};

function withSource(url: string, source?: string): string {
  return source ? `${url}?source=${encodeURIComponent(source)}` : url;
}

@Injectable({
  providedIn: 'root',
})
export class BackendConfigurationPnCompliancesService {
  constructor(private apiBaseService: ApiBaseService) {}

  getAllCompliances(
    model: CompliancesRequestModel
  ): Observable<OperationDataResult<Paged<ComplianceModel>>> {
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

  /**
   * `source: 'compliance'` (#1300) is sent by the compliance pages; the server
   * then refuses to complete a task dated after today. Omitted, the request is
   * byte-identical to before.
   */
  updateCase(
    model: ReplyRequest,
    templateId: number,
    source?: 'compliance'
  ): Observable<OperationResult> {
    return this.apiBaseService.put<ReplyRequest>(
      withSource(BackendConfigurationPnCompliancesMethods.GetCases, source),
      model
    );
  }

  /**
   * Shared by the calendar (no `source` — it completes future occurrences
   * early on purpose) and Detaljer (`source: 'compliance'`, #1300).
   */
  updateCaseFromCalendar(
    model: ReplyRequest,
    templateId: number,
    source?: 'compliance'
  ): Observable<OperationResult> {
    return this.apiBaseService.put<ReplyRequest>(
      withSource(BackendConfigurationPnCompliancesMethods.UpdateCaseFromCalendar, source),
      model
    );
  }

  deleteCompliance(id: number): Observable<OperationResult> {
    return this.apiBaseService.delete(BackendConfigurationPnCompliancesMethods.DeleteCompliance + '/' + id);
  }
}
