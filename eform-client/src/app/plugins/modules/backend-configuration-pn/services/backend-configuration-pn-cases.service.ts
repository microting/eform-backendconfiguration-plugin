import { Observable } from 'rxjs';
import { OperationResult, ReplyRequest} from 'src/app/common/models';
import { Injectable } from '@angular/core';
import { ApiBaseService } from 'src/app/common/services';

export let ItemsPlanningPnCasesMethods = {
  GetCases: 'api/backend-configuration-pn/cases',
};
@Injectable({
  providedIn: 'root',
})
export class BackendConfigurationPnCasesService {
  constructor(private apiBaseService: ApiBaseService) {}

  updateCase(
    model: ReplyRequest,
    templateId: number
  ): Observable<OperationResult> {
    return this.apiBaseService.put<ReplyRequest>(
      ItemsPlanningPnCasesMethods.GetCases,
      model
    );
  }
}
