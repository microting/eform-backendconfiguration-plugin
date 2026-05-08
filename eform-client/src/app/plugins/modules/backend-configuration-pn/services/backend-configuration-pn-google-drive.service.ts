import {Injectable} from '@angular/core';
import {Observable} from 'rxjs';
import {ApiBaseService} from 'src/app/common/services';
import {OperationDataResult} from 'src/app/common/models';
import {
  CalendarTaskAttachment,
  GoogleDrivePickerToken,
  GoogleDriveStatus,
} from '../models/calendar';

export let BackendConfigurationPnGoogleDriveMethods = {
  Status: 'api/backend-configuration-pn/google-drive/status',
  Start: 'api/backend-configuration-pn/google-drive/start',
  PickerToken: 'api/backend-configuration-pn/google-drive/picker-token',
  Attach: 'api/backend-configuration-pn/google-drive/attach',
};

/**
 * Google Drive integration service. Wraps the four `/google-drive/*` endpoints
 * introduced in PR-3 + PR-4 of the design:
 * `docs/superpowers/specs/2026-05-07-google-drive-integration-design.md`.
 *
 * The connect-and-pick flow:
 * 1. {@link getStatus} — does the user already have a token?
 * 2. If not, {@link start} returns the proxy URL the frontend opens in a
 *    popup; the proxy 302s back to the customer-side `/oauth-finish` which
 *    posts a message to the modal so the dance continues.
 * 3. {@link getPickerToken} — fresh access token for instantiating the
 *    Google Picker JS SDK client-side.
 * 4. {@link attachFile} — downloads the picked file into the calendar's
 *    `AreaRulePlanning` attachment list.
 */
@Injectable({providedIn: 'root'})
export class BackendConfigurationPnGoogleDriveService {
  constructor(private apiBaseService: ApiBaseService) {}

  getStatus(): Observable<OperationDataResult<GoogleDriveStatus>> {
    return this.apiBaseService.get(BackendConfigurationPnGoogleDriveMethods.Status);
  }

  start(): Observable<OperationDataResult<string>> {
    return this.apiBaseService.post(BackendConfigurationPnGoogleDriveMethods.Start, {});
  }

  getPickerToken(): Observable<OperationDataResult<GoogleDrivePickerToken>> {
    return this.apiBaseService.get(BackendConfigurationPnGoogleDriveMethods.PickerToken);
  }

  attachFile(
    areaRulePlanningId: number,
    driveFileId: string,
  ): Observable<OperationDataResult<CalendarTaskAttachment>> {
    return this.apiBaseService.post(
      BackendConfigurationPnGoogleDriveMethods.Attach,
      {areaRulePlanningId, driveFileId},
    );
  }
}
