import {Injectable} from '@angular/core';
import {Observable} from 'rxjs';
import {
  OperationDataResult,
  SortModel,
} from 'src/app/common/models';
import {BackendConfigurationPnAreasService} from '../../../../services';
import {AreaRuleSimpleModel} from '../../../../models';
import {updateTableSort} from 'src/app/common/helpers';
import {Store} from '@ngrx/store';
import {
  selectAreaRulesPagination,
  updateAreaRulesPagination
} from '../../../../state';

@Injectable({providedIn: 'root'})
export class AreaRulesStateService {
  private selectAreaRulesPagination$ = this.store.select(selectAreaRulesPagination);
  constructor(
    private store: Store,
    private service: BackendConfigurationPnAreasService,
  ) {
    this.selectAreaRulesPagination$.subscribe(x => this.currentPagination = x);
  }

  private propertyAreaId: number;
  currentPagination: SortModel;

  setPropertyAreaId(propertyAreaId: number) {
    this.propertyAreaId = propertyAreaId;
  }

  getAllAreaRules():
    Observable<OperationDataResult<AreaRuleSimpleModel[]>> {
    return this.service
      .getAreaRules(
        {
        sort: this.currentPagination.sort,
        isSortDsc: this.currentPagination.isSortDsc,
        propertyAreaId: this.propertyAreaId,
      });
  }

  onSortTable(sort: string) {
    const localPageSetting = updateTableSort(
      sort,
      this.currentPagination.sort,
      this.currentPagination.isSortDsc
    );
    this.store.dispatch(updateAreaRulesPagination({
      ...this.currentPagination,
      sort: localPageSetting.sort,
      isSortDsc: localPageSetting.isSortDsc,
    }));
  }
}
