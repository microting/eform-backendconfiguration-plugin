import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import {OperationDataResult,} from 'src/app/common/models';
import { ApiBaseService } from 'src/app/common/services';


export let BackendConfigurationPnReportsMethods = {
  WordReport: 'api/backend-configuration-pn/report/word',
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
}
