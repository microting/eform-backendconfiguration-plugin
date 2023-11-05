import {Injectable} from '@angular/core';
// import {
//   AreaRulesStore,
//   AreaRulesQuery,
// } from './';
import {Observable} from 'rxjs';
import {
  OperationDataResult,
} from 'src/app/common/models';
import {BackendConfigurationPnAreasService} from '../../../../services';
import {AreaRuleSimpleModel} from '../../../../models';
import {updateTableSort} from 'src/app/common/helpers';

@Injectable({providedIn: 'root'})
export class AreaRulesStateService {
  constructor(
    // public store: AreaRulesStore,
    private service: BackendConfigurationPnAreasService,
    // private query: AreaRulesQuery
  ) {
  }

  private propertyAreaId: number;

  setPropertyAreaId(propertyAreaId: number) {
    this.propertyAreaId = propertyAreaId;
  }

  getAllAreaRules():
    Observable<OperationDataResult<AreaRuleSimpleModel[]>> {
    return this.service
      .getAreaRules({
        sort: 'Id',
        isSortDsc: false,
        // ...this.query.pageSetting.pagination,
        propertyAreaId: this.propertyAreaId,
      });
  }

  // getActiveSort(): Observable<string> {
  //   return this.query.selectActiveSort$;
  // }
  //
  // getActiveSortDirection(): Observable<'asc' | 'desc'> {
  //   return this.query.selectActiveSortDirection$;
  // }

  onSortTable(sort: string) {
  //   const localPageSetting = updateTableSort(
  //     sort,
  //     this.query.pageSetting.pagination.sort,
  //     this.query.pageSetting.pagination.isSortDsc
  //   );
  //   this.store.update((state) => ({
  //     pagination: {
  //       ...state.pagination,
  //       isSortDsc: localPageSetting.isSortDsc,
  //       sort: localPageSetting.sort,
  //     },
  //   }));
  }
}
