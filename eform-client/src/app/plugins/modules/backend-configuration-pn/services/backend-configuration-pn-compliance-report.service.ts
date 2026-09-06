import {Injectable} from '@angular/core';
import {HttpBackend, HttpClient, HttpResponse} from '@angular/common/http';
import {Observable, throwError} from 'rxjs';
import {catchError, map, switchMap, take, tap} from 'rxjs/operators';
import {ToastrService} from 'ngx-toastr';
import {TranslateService} from '@ngx-translate/core';
import {Store} from '@ngrx/store';
import {ApiBaseService} from 'src/app/common/services';
import {OperationDataResult, OperationResult} from 'src/app/common/models';
import {selectBearerToken} from 'src/app/state/auth/auth.selector';
import {
  ComplianceReportExportRequestModel,
  ComplianceReportOverviewModel,
  ComplianceReportOverviewRequestModel,
  ComplianceReportPagedModel,
  ComplianceReportRequestModel,
  ComplianceReportTagGroupModel,
} from '../models';

export let BackendConfigurationPnComplianceReportMethods = {
  // Its own controller prefix, not the calendar's — the standalone page is
  // not a calendar view mode (#1160 decision 1).
  Index: 'api/backend-configuration-pn/compliance-report/index',
  // The Oversigt aggregation (#1162). Unpaged and unsorted by decision — one
  // row per property plus a weighted totals row.
  Overview: 'api/backend-configuration-pn/compliance-report/overview',
  // The Rapport projection (#1166). UNPAGED and UNSORTED by decision: it
  // ignores `sort`/`pageIndex`/`pageSize` on the request body and applies the
  // service's own row cap instead, because Rapport groups the whole filtered
  // set and every sub-report is rendered whole.
  EformColumns: 'api/backend-configuration-pn/compliance-report/eform-columns',
  // The server-side export (#1169 endpoint, #1189 wiring). Renders the
  // current view as PDF or CSV and answers with the file bytes plus a
  // `Content-Disposition` carrying the file name.
  Export: 'api/backend-configuration-pn/compliance-report/export',
};

/** What `export()` resolves to: the bytes and the server-chosen file name. */
export interface ComplianceExportResult {
  blob: Blob;
  fileName: string;
}

/**
 * File name out of a `Content-Disposition` header.
 *
 * ORDER MATTERS. The server emits
 * `attachment; filename="<ascii>"; filename*=UTF-8''<percent-encoded>`
 * (`ComplianceExportFileNaming.BuildContentDisposition`), and the plain
 * `filename=` half is LOSSY: `MakeAsciiFallback` maps every non-ASCII
 * character to `_`, so `Miljøtilsyn` arrives as `Milj_tilsyn` there. The RFC
 * 5987 `filename*=` form is read first; `filename=` is the fallback for a
 * server that sent only that; the caller's client-built name is the last
 * resort for no header at all (a proxy that strips it, or a CORS setup that
 * does not expose it).
 */
export function parseContentDispositionFileName(header: string | null, fallback: string): string {
  if (!header) {
    return fallback;
  }
  const extended = /filename\*\s*=\s*utf-8''([^;]+)/i.exec(header);
  if (extended) {
    try {
      const decoded = decodeURIComponent(extended[1].trim()).trim();
      if (decoded) {
        return decoded;
      }
    } catch {
      // Malformed percent-encoding — fall through to the plain form.
    }
  }
  const quoted = /filename\s*=\s*"((?:[^"\\]|\\.)*)"/i.exec(header);
  if (quoted) {
    const unescaped = quoted[1].replace(/\\(.)/g, '$1').trim();
    if (unescaped) {
      return unescaped;
    }
  }
  const bare = /filename\s*=\s*([^;]+)/i.exec(header);
  if (bare) {
    const value = bare[1].trim();
    if (value) {
      return value;
    }
  }
  return fallback;
}

/**
 * Data access for the standalone Compliance page (#1160): the three query
 * endpoints plus the export.
 */
@Injectable({providedIn: 'root'})
export class BackendConfigurationPnComplianceReportService {
  /**
   * An `HttpClient` wired straight to the backend, so `export()` runs with NO
   * interceptors — see the ERROR PATH note on `export()` for why the global
   * chain cannot be used for a blob download that may fail.
   */
  private readonly rawHttp: HttpClient;

  constructor(
    private apiBaseService: ApiBaseService,
    private toastr: ToastrService,
    private translate: TranslateService,
    private store: Store,
    httpBackend: HttpBackend,
  ) {
    this.rawHttp = new HttpClient(httpBackend);
  }

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

  /**
   * The Rapport projection (#1166): tag groups → template groups → an ordered
   * column schema plus one KEYED cell bag per case.
   *
   * Takes the same request model as `index()` — the shared filter set — and
   * ignores its paging and sorting fields server-side. `postNoToast` + the
   * shared `notifyError` matches the two siblings: one toast on failure, and
   * the caller still sees `success: false` so it can decide what to leave on
   * screen.
   */
  eformColumns(
    model: ComplianceReportRequestModel
  ): Observable<OperationDataResult<ComplianceReportTagGroupModel[]>> {
    return this.apiBaseService
      .postNoToast<ComplianceReportTagGroupModel[]>(
        BackendConfigurationPnComplianceReportMethods.EformColumns,
        model
      )
      .pipe(tap((res) => this.notifyError(res)));
  }

  /**
   * The server-side export (#1169 / #1189): the current view as PDF or CSV.
   *
   * Uses a bare `HttpClient` over `HttpBackend` (`rawHttp`) rather than the
   * injected `HttpClient` or `ApiBaseService.postBlobData`, for two reasons:
   *
   *  - `postBlobData` returns the response BODY only, so the
   *    `Content-Disposition` header — the one place the server-built file
   *    name lives — would be unreadable through it. `observe: 'response'`
   *    keeps the headers.
   *  - ERROR PATH. The core registers `HttpErrorInterceptor` twice
   *    (`app.declarations.ts` + `SharedPnModule`). On a 400 the inner copy
   *    rethrows `''`; the outer copy sees no `status`, re-issues the POST
   *    immediately and then every 15 s up to 5 times, then completes with
   *    `EMPTY` — so the subscriber never errors and `catchError` never runs.
   *
   * What the bypass gives up: no global loader overlay for this call (the
   * page drives its own busy state), no 401 → logout / 403 → token-refresh
   * handling, and no Sentry capture — a stale token yields "Export failed"
   * here, and the next interceptor-path call still signs the user out or
   * refreshes the token.
   *
   * Skipping the chain means skipping `JwtInterceptor` too, so the bearer
   * token is attached explicitly, read from the same store selector that
   * interceptor uses. The relative `api/...` path resolves against
   * `<base href="/">` exactly as `ApiBaseService`'s own calls do.
   *
   * On failure the toast is raised HERE, generically (#1189 decision 8a; the
   * server's own message stays in the server log), and the error is rethrown
   * so the caller's `finalize` can re-enable the Download button.
   */
  export(
    model: ComplianceReportExportRequestModel,
    fallbackFileName: string
  ): Observable<ComplianceExportResult> {
    return this.store.select(selectBearerToken).pipe(
      take(1),
      switchMap((token) =>
        this.rawHttp.post(BackendConfigurationPnComplianceReportMethods.Export, model, {
          observe: 'response',
          responseType: 'blob',
          headers: token ? {Authorization: `Bearer ${token}`} : {},
        })
      ),
      map((res: HttpResponse<Blob>) => ({
        blob: res.body ?? new Blob(),
        fileName: parseContentDispositionFileName(
          res.headers.get('Content-Disposition'),
          fallbackFileName
        ),
      })),
      catchError((err: unknown) => {
        this.toastr.error(this.translate.instant('Export failed'));
        return throwError(() => err);
      })
    );
  }
}
