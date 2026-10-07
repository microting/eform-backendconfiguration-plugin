import {Injectable} from '@angular/core';
import {Observable} from 'rxjs';
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

/** The Indbakke: PDFs mailed to the tenant's archive address, reviewed and filed by a person. */
@Injectable({
  providedIn: 'root',
})
export class BackendConfigurationPnInboxService {
  constructor(private apiBaseService: ApiBaseService) {}

  /** `status` null = the server's default view (open documents plus the last week's filed ones). */
  list(status: number | null, search: string): Observable<OperationDataResult<InboxListItemModel[]>> {
    return this.apiBaseService.get<InboxListItemModel[]>(BackendConfigurationPnInboxMethods.Inbox, {
      status,
      search: search?.trim() || null,
    });
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

  /** `block` also adds a Block rule for the sender's address, so later mail from it is refused. */
  reject(id: number, block = false): Observable<OperationResult> {
    return this.apiBaseService.post(`${BackendConfigurationPnInboxMethods.Inbox}/${id}/reject?block=${block}`, {});
  }

  /** No toast: the settings page shows a failure (hub down, not configured) inline instead. */
  getSettings(): Observable<OperationDataResult<InboxSettingsModel>> {
    return this.apiBaseService.getNoToast<InboxSettingsModel>(BackendConfigurationPnInboxMethods.Settings);
  }

  /** Full replace of the blocked senders. */
  updateSettings(model: InboxSettingsUpdateModel): Observable<OperationResult> {
    return this.apiBaseService.put(BackendConfigurationPnInboxMethods.Settings, model);
  }

  /** Only the tenant's first user may rotate; anyone else gets 403. */
  rotateAddress(): Observable<OperationDataResult<InboxSettingsModel>> {
    return this.apiBaseService.post<InboxSettingsModel>(
      `${BackendConfigurationPnInboxMethods.Settings}/rotate-address`,
      {}
    );
  }
}
