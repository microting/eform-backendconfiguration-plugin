import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { OperationDataResult, OperationResult } from 'src/app/common/models';
import { BackendConfigurationBaseSettingsModel } from '../models';
import { ApiBaseService } from 'src/app/common/services';

export let BackendConfigurationSettingsMethods = {
  ItemsPlanningSettings: 'api/backend-configuration-pn/settings',
};
@Injectable()
export class BackendConfigurationPnSettingsService {
  constructor(private apiBaseService: ApiBaseService) {}

  getAllSettings(): Observable<
    OperationDataResult<BackendConfigurationBaseSettingsModel>
  > {
    return this.apiBaseService.get(
      BackendConfigurationSettingsMethods.ItemsPlanningSettings
    );
  }
  updateSettings(
    model: BackendConfigurationBaseSettingsModel
  ): Observable<OperationResult> {
    return this.apiBaseService.post(
      BackendConfigurationSettingsMethods.ItemsPlanningSettings,
      model
    );
  }
}
