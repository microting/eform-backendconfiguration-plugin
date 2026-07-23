import {Component, OnDestroy, OnInit, inject} from '@angular/core';
import {AutoUnsubscribe} from 'ngx-auto-unsubscribe';
import {ActivatedRoute, Router} from '@angular/router';
import {AppMenuStateService} from 'src/app/common/store';
import {PaginationModel} from 'src/app/common/models';
import {Store} from '@ngrx/store';
import {Subscription, skip} from 'rxjs';
import {AdhocTaskModel} from '../../../../models';
import {selectAdhocFilters} from '../../../../state';
import {AdhocStateService} from '../store';

/**
 * Top bar + Overblik data orchestration for the "Adhoc overblik" dashboard
 * (M5/F4, extended F5). The Overblik view (toolbar filters + table) is
 * embedded directly in this component's own template (not a routed child -
 * see the routing decision note in adhoc.routing.ts); only Historik is an
 * actual nested route, rendered through the `<router-outlet>` below.
 *
 * The drawer (F7) and modals (F8) are wired up as those components land -
 * until then the row/toolbar actions that would open them are inert stubs
 * (same pattern F4 used for "Ny opgave").
 */
@AutoUnsubscribe()
@Component({
  selector: 'app-adhoc-container',
  templateUrl: './adhoc-container.component.html',
  styleUrls: ['./adhoc-container.component.scss'],
  standalone: false
})
export class AdhocContainerComponent implements OnInit, OnDestroy {
  private router = inject(Router);
  private route = inject(ActivatedRoute);
  private appMenuStateService = inject(AppMenuStateService);
  private store = inject(Store);
  public adhocStateService = inject(AdhocStateService);

  // Resolved once in ngOnInit — getTitleByUrl subscribes to the menu store
  // internally without unsubscribing, so calling it from the template would
  // leak one subscription per change-detection tick (same rationale as
  // TaskManagementContainerComponent).
  public pageTitle: string = '';

  tasks: AdhocTaskModel[] = [];
  counts = {open: 0, completed: 0, archived: 0};

  private selectAdhocFilters$ = this.store.select(selectAdhocFilters);

  titleSub$: Subscription;
  filtersSub$: Subscription;
  getTasksSub$: Subscription;
  loadReferenceDataSub$: Subscription;

  get status() {
    return this.adhocStateService.currentFilters?.status ?? 'open';
  }

  get pagination() {
    return this.adhocStateService.currentPagination;
  }

  ngOnInit(): void {
    this.titleSub$ = this.appMenuStateService.leftAppMenus$.subscribe(() => {
      this.pageTitle = this.appMenuStateService.getTitleByUrl(this.router.url);
    });

    // Any filter change (toolbar filters, F6) re-fetches page 1 - the state
    // facade (AdhocStateService.updateFilters) already resets pagination to
    // page 1 as part of the same user action, so this single subscription
    // is enough (no separate pagination subscription - mirrors
    // TaskManagementContainerComponent's own skip(1) pattern).
    this.filtersSub$ = this.selectAdhocFilters$.pipe(skip(1)).subscribe(() => this.updateTable());

    this.adhocStateService.resetReferenceData();
    this.loadReferenceDataSub$ = this.adhocStateService.loadProperties().subscribe();
    this.adhocStateService.loadTags().subscribe();

    this.updateTable();
  }

  ngOnDestroy(): void {
  }

  get isHistoryView(): boolean {
    return this.route.snapshot.firstChild?.routeConfig?.path === 'history';
  }

  updateTable(): void {
    this.getTasksSub$ = this.adhocStateService.getTasks().subscribe((data) => {
      if (data && data.success && data.model) {
        this.tasks = data.model.entities;
        this.counts = {
          open: data.model.openCount,
          completed: data.model.completedCount,
          archived: data.model.archivedCount,
        };
      }
    });
  }

  onPaginationChanged(pagination: PaginationModel): void {
    this.adhocStateService.changePage(pagination);
    this.updateTable();
  }

  // TODO(F7): open the "Ny opgave" drawer (MatDialog, create mode) once
  // AdhocTaskDrawerComponent exists.
  openCreateTask(): void {
  }

  // TODO(F7): open AdhocTaskDrawerComponent in 'view' mode.
  onViewTask(task: AdhocTaskModel): void {
  }

  // TODO(F7): open AdhocTaskDrawerComponent in 'edit' mode.
  onEditTask(task: AdhocTaskModel): void {
  }

  // TODO(F8): open AdhocCopyModalComponent.
  onCopyTask(task: AdhocTaskModel): void {
  }

  // TODO(F8): open AdhocDeleteModalComponent.
  onDeleteTask(task: AdhocTaskModel): void {
  }

  // TODO(F8): open AdhocCompleteModalComponent.
  onCompleteTask(task: AdhocTaskModel): void {
  }
}
