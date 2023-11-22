import {Injectable} from '@angular/core';
import {Observable} from 'rxjs';
import {
  CommonPaginationState,
  OperationDataResult,
} from 'src/app/common/models';
import {BackendConfigurationPnAreasService} from '../../../../services';
import {AreaRuleSimpleModel} from '../../../../models';
import {updateTableSort} from 'src/app/common/helpers';
import {Store} from '@ngrx/store';
import {
  selectAreaRulesPagination
} from '../../../../state/area-rules/area-rules.selector';

@Injectable({providedIn: 'root'})
export class AreaRulesStateService {
  private selectAreaRulesPagination$ = this.store.select(selectAreaRulesPagination);
  constructor(
    private store: Store,
    private service: BackendConfigurationPnAreasService,
  ) {
  }

  private propertyAreaId: number;

  setPropertyAreaId(propertyAreaId: number) {
    this.propertyAreaId = propertyAreaId;
  }

  getAllAreaRules():
    Observable<OperationDataResult<AreaRuleSimpleModel[]>> {
    let pagination = new CommonPaginationState();
    this.selectAreaRulesPagination$.subscribe((x) => (pagination = x));
    return this.service
      .getAreaRules(
        {
        sort: pagination.sort,
        isSortDsc: pagination.isSortDsc,
        propertyAreaId: this.propertyAreaId,
      });
  }

  onSortTable(sort: string) {
    let currentPagination: CommonPaginationState;
    this.selectAreaRulesPagination$.subscribe((x) => (currentPagination = x));
    const localPageSetting = updateTableSort(
      sort,
      currentPagination.sort,
      currentPagination.isSortDsc
    );
    this.store.dispatch({
      type: '[AreaRules] Update pagination',
      payload: {
        sort: localPageSetting.sort,
        isSortDsc: localPageSetting.isSortDsc,
        pageIndex: 0,
        offset: 0,
        propertyAreaId: this.propertyAreaId,
      },
    })
  }
}
