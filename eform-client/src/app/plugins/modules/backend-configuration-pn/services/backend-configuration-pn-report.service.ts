import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import {CommonDictionaryModel, OperationDataResult, OperationResult, SharedTagModel,} from 'src/app/common/models';
import { ApiBaseService } from 'src/app/common/services';
import {ReportEformPnModel, ReportPnGenerateModel} from 'src/app/plugins/modules/items-planning-pn/models';
import {ItemsPlanningPnPlanningsMethods, ItemsPlanningTagsMethods} from 'src/app/plugins/modules/items-planning-pn/services';


export let BackendConfigurationPnReportsMethods = {
  WordReport: 'api/backend-configuration-pn/report/word',
  Reports: 'api/backend-configuration-pn/report/reports',
  DeleteCase: 'api/items-planning-pn/plannings-case/delete',
  Tags: 'api/items-planning-pn/tags',
};

@Injectable({
  providedIn: 'root',
})
  export class BackendConfigurationPnReportService {
  constructor(private apiBaseService: ApiBaseService) {}

  downloadReport(propertyId: number, areaId: number, selectedYear: number): Observable<any> {
    return this.apiBaseService.getBlobData(BackendConfigurationPnReportsMethods.WordReport, {
      propertyId, areaId, year: selectedYear,
    });
  }

  generateReport(
    model: ReportPnGenerateModel
  ): Observable<OperationDataResult<ReportEformPnModel[]>> {
    return this.apiBaseService.post(
      BackendConfigurationPnReportsMethods.Reports,
      model
    );
  }

  downloadFileReport(model: ReportPnGenerateModel): Observable<string | Blob> {
    return this.apiBaseService.getBlobData(
      BackendConfigurationPnReportsMethods.Reports + '/file',
      model
    );
  }


  deleteCase(planningCaseId: number): Observable<OperationResult> {
    return this.apiBaseService.delete(
      BackendConfigurationPnReportsMethods.DeleteCase,
      { planningCaseId: planningCaseId }
    );
  }

  getPlanningsTags(): Observable<OperationDataResult<CommonDictionaryModel[]>> {
    return this.apiBaseService.get<SharedTagModel[]>(
      BackendConfigurationPnReportsMethods.Tags
    );
  }

}
