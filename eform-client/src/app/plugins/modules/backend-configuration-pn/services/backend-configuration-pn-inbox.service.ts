import {Injectable} from '@angular/core';
import {Observable} from 'rxjs';
import {map} from 'rxjs/operators';
import {OperationDataResult, OperationResult} from 'src/app/common/models';
import {ApiBaseService} from 'src/app/common/services';
import {
  FileInboxDocumentModel,
  InboxListItemModel,
  InboxSettingsModel,
  InboxSettingsUpdateModel,
} from '../models';

export const BackendConfigurationPnInboxMethods = {
  Inbox: 'api/backend-configuration-pn/inbox',
  Settings: 'api/backend-configuration-pn/inbox/settings',
};

/**
 * The API stores UTC but serialises the DateTime without an offset, which the `date` pipe would read
 * as local time. Mark such values as UTC; leave values that already carry an offset alone.
 */
export function inboxAsUtc(value: string | null): string | null {
  if (!value || /([zZ]|[+-]\d{2}:?\d{2})$/.test(value)) {
    return value;
  }
  return `${value}Z`;
}

/** The Indbakke: PDFs mailed to the tenant's archive address, reviewed and filed by a person. */
@Injectable({
  providedIn: 'root',
})
export class BackendConfigurationPnInboxService {
  constructor(private apiBaseService: ApiBaseService) {}

  /** `status` null = the server's default view (open documents plus the last week's filed ones). */
  list(status: number | null, search: string): Observable<OperationDataResult<InboxListItemModel[]>> {
    return this.apiBaseService
      .get<InboxListItemModel[]>(BackendConfigurationPnInboxMethods.Inbox, {
        status,
        search: search?.trim() || null,
      })
      .pipe(
        map((res: OperationDataResult<InboxListItemModel[]>) => {
          if (res?.model) {
            res.model = res.model.map(d => ({...d, receivedAt: inboxAsUtc(d.receivedAt), readyBy: inboxAsUtc(d.readyBy)}));
          }
          return res;
        })
      );
  }

  getPdf(id: number): Observable<Blob> {
    return this.apiBaseService.getBlobData(`${BackendConfigurationPnInboxMethods.Inbox}/${id}/file`);
  }

  file(id: number, model: FileInboxDocumentModel): Observable<OperationResult> {
    return this.apiBaseService.post(`${BackendConfigurationPnInboxMethods.Inbox}/${id}/file`, model);
  }

  undo(id: number): Observable<OperationResult> {
    return this.apiBaseService.post(`${BackendConfigurationPnInboxMethods.Inbox}/${id}/undo`, {});
  }

  reject(id: number): Observable<OperationResult> {
    return this.apiBaseService.post(`${BackendConfigurationPnInboxMethods.Inbox}/${id}/reject`, {});
  }

  approveSender(id: number): Observable<OperationResult> {
    return this.apiBaseService.post(`${BackendConfigurationPnInboxMethods.Inbox}/${id}/approve-sender`, {});
  }

  /** `block` also adds a Block rule for the sender's address. */
  rejectSender(id: number, block: boolean): Observable<OperationResult> {
    return this.apiBaseService.post(
      `${BackendConfigurationPnInboxMethods.Inbox}/${id}/reject-sender?block=${block}`,
      {}
    );
  }

  /** No toast: the settings page shows a failure (hub down, not configured) inline instead. */
  getSettings(): Observable<OperationDataResult<InboxSettingsModel>> {
    return this.apiBaseService.getNoToast<InboxSettingsModel>(BackendConfigurationPnInboxMethods.Settings);
  }

  /** Full replace of the unknown-sender policy and the sender rules. */
  updateSettings(model: InboxSettingsUpdateModel): Observable<OperationResult> {
    return this.apiBaseService.put(BackendConfigurationPnInboxMethods.Settings, model);
  }

  rotateAddress(): Observable<OperationDataResult<InboxSettingsModel>> {
    return this.apiBaseService.post<InboxSettingsModel>(
      `${BackendConfigurationPnInboxMethods.Settings}/rotate-address`,
      {}
    );
  }
}
