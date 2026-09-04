import {Injectable} from '@angular/core';
import {Observable} from 'rxjs';
import {tap} from 'rxjs/operators';
import {ToastrService} from 'ngx-toastr';
import {TranslateService} from '@ngx-translate/core';
import {ApiBaseService} from 'src/app/common/services';
import {OperationDataResult, OperationResult} from 'src/app/common/models';
import {
  ComplianceReportOverviewModel,
  ComplianceReportOverviewRequestModel,
  ComplianceReportPagedModel,
  ComplianceReportRequestModel,
} from '../models';

export let BackendConfigurationPnComplianceReportMethods = {
  // Its own controller prefix, not the calendar's — the standalone page is
  // not a calendar view mode (#1160 decision 1).
  Index: 'api/backend-configuration-pn/compliance-report/index',
  // The Oversigt aggregation (#1162). Unpaged and unsorted by decision — one
  // row per property plus a weighted totals row.
  Overview: 'api/backend-configuration-pn/compliance-report/overview',
};

/**
 * Data access for the standalone Compliance page (#1160). Two endpoints today;
 * #1169 (export) adds a third.
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

  /**
   * The Oversigt aggregation (#1162): one row per property plus the weighted
   * totals row, unpaged.
   *
   * The request model carries NO status, no paging and no sort — see
   * `ComplianceReportOverviewRequestModel`. `postNoToast` + the shared
   * `notifyError` matches `index()`: one toast on failure, and the caller still
   * sees `success: false` so it can decide what to leave on screen.
   */
  overview(
    model: ComplianceReportOverviewRequestModel
  ): Observable<OperationDataResult<ComplianceReportOverviewModel>> {
    return this.apiBaseService
      .postNoToast<ComplianceReportOverviewModel>(
        BackendConfigurationPnComplianceReportMethods.Overview,
        model
      )
      .pipe(tap((res) => this.notifyError(res)));
  }
}
