import {Injectable} from '@angular/core';
import {Observable} from 'rxjs';
import {CommonDictionaryModel, OperationDataResult, OperationResult, SharedTagModel,} from 'src/app/common/models';
import {ApiBaseService} from 'src/app/common/services';
import {NewReportEformPnModel, ReportEformPnModel, ReportPnGenerateModel} from 'src/app/plugins/modules/backend-configuration-pn/models';


export let BackendConfigurationPnReportsMethods = {
  WordReport: 'api/backend-configuration-pn/report/word',
  Reports: 'api/backend-configuration-pn/report/reports',
  NewReports: 'api/backend-configuration-pn/report/new-reports',
  DeleteCase: 'api/items-planning-pn/plannings-case/delete',
  Tags: 'api/items-planning-pn/tags',
};

@Injectable({
  providedIn: 'root',
})
export class BackendConfigurationPnReportService {
  constructor(private apiBaseService: ApiBaseService) {
  }

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


  generateNewReport(
    model: ReportPnGenerateModel
  ): Observable<OperationDataResult<NewReportEformPnModel[]>> {
    return this.apiBaseService.post(
      BackendConfigurationPnReportsMethods.NewReports,
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
      {planningCaseId: planningCaseId}
    );
  }

  getPlanningsTags(): Observable<OperationDataResult<CommonDictionaryModel[]>> {
    return this.apiBaseService.get<SharedTagModel[]>(
      BackendConfigurationPnReportsMethods.Tags
    );
  }

}
