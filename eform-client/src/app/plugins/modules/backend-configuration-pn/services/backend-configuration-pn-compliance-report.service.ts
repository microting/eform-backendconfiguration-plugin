import {Injectable} from '@angular/core';
import {Observable} from 'rxjs';
import {tap} from 'rxjs/operators';
import {ToastrService} from 'ngx-toastr';
import {TranslateService} from '@ngx-translate/core';
import {ApiBaseService} from 'src/app/common/services';
import {OperationDataResult, OperationResult} from 'src/app/common/models';
import {ComplianceReportPagedModel, ComplianceReportRequestModel} from '../models';

export let BackendConfigurationPnComplianceReportMethods = {
  // Its own controller prefix, not the calendar's — the standalone page is
  // not a calendar view mode (#1160 decision 1).
  Index: 'api/backend-configuration-pn/compliance-report/index',
};

/**
 * Data access for the standalone Compliance page (#1160). One endpoint today;
 * #1162 (Oversigt aggregation) and #1169 (export) add siblings here.
 */
@Injectable({providedIn: 'root'})
export class BackendConfigurationPnComplianceReportService {
  constructor(
    private apiBaseService: ApiBaseService,
    private toastr: ToastrService,
    private translate: TranslateService,
  ) {}

  private notifyError(res: OperationResult): void {
    if (!res || !res.success) {
      this.toastr.error(`${this.translate.instant('Error')} [${(res && res.message) || 'unknown'}]`);
    }
  }

  index(
    model: ComplianceReportRequestModel
  ): Observable<OperationDataResult<ComplianceReportPagedModel>> {
    return this.apiBaseService
      .postNoToast<ComplianceReportPagedModel>(BackendConfigurationPnComplianceReportMethods.Index, model)
      .pipe(tap((res) => this.notifyError(res)));
  }
}
