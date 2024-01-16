import {Injectable} from '@angular/core';
import {Observable} from 'rxjs';
import {CommonPaginationState, OperationDataResult, Paged} from 'src/app/common/models';
import {updateTableSort} from 'src/app/common/helpers';
import {map} from 'rxjs/operators';
import {BackendConfigurationPnAreasService,} from '../../../../services';
import {TaskWorkerModel} from '../../../../models';
import {Store} from '@ngrx/store';
import {
  selectTaskWorkerAssignmentPagination, taskWorkerAssignmentUpdatePagination
} from '../../../../state';

@Injectable({providedIn: 'root'})
export class TaskWorkerAssignmentsStateService {
  private selectTaskWorkerAssignmentPagination$ = this.store.select(selectTaskWorkerAssignmentPagination);
  currentPagination: CommonPaginationState;

  constructor(
    private store: Store,
    private service: BackendConfigurationPnAreasService,
  ) {
    this.selectTaskWorkerAssignmentPagination$.subscribe(x => this.currentPagination = x);
  }

  private _siteId: number;

  public set siteId(value: number) {
    this._siteId = value;
  }

  public get siteId() {
    return this._siteId;
  }

  getTaskWorkerAssignments(): Observable<OperationDataResult<Paged<TaskWorkerModel>>> {
    return this.service.getTaskWorkerAssignments(this._siteId, this.currentPagination).pipe(
      map((response) => {
        return response;
      })
    );
  }

  onSortTable(sort: string) {
    const localPageSettings = updateTableSort(
      sort,
      this.currentPagination.sort,
      this.currentPagination.isSortDsc
    );
    this.store.dispatch(taskWorkerAssignmentUpdatePagination({
      ...this.currentPagination,
      ...localPageSettings,
    }));
  }
}
