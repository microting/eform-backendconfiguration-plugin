import {Component, OnDestroy, OnInit, inject} from '@angular/core';
import {AutoUnsubscribe} from 'ngx-auto-unsubscribe';
import {ActivatedRoute, Router} from '@angular/router';
import {AppMenuStateService} from 'src/app/common/store';
import {PaginationModel} from 'src/app/common/models';
import {Store} from '@ngrx/store';
import {Subscription, skip} from 'rxjs';
import {MatDialog} from '@angular/material/dialog';
import {Overlay} from '@angular/cdk/overlay';
import {dialogConfigHelper} from 'src/app/common/helpers';
import {AdhocTaskModel} from '../../../../models';
import {selectAdhocFilters} from '../../../../state';
import {BackendConfigurationPnAdhocService} from '../../../../services';
import {AdhocStateService} from '../store';
import {AdhocTaskDrawerComponent, AdhocTaskDrawerCloseResult, AdhocTaskDrawerData} from '../adhoc-task-drawer/adhoc-task-drawer.component';
import {AdhocDeleteModalComponent} from '../adhoc-delete-modal/adhoc-delete-modal.component';
import {AdhocCopyModalComponent} from '../adhoc-copy-modal/adhoc-copy-modal.component';
import {AdhocCompleteModalComponent} from '../adhoc-complete-modal/adhoc-complete-modal.component';

/**
 * Top bar + Overblik data orchestration for the "Adhoc overblik" dashboard
 * (M5/F4, extended F5/F7/F8). The Overblik view (toolbar filters + table)
 * is embedded directly in this component's own template (not a routed
 * child - see the routing decision note in adhoc.routing.ts); only
 * Historik is an actual nested route, rendered through the
 * `<router-outlet>` below.
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
  private dialog = inject(MatDialog);
  private overlay = inject(Overlay);
  private adhocService = inject(BackendConfigurationPnAdhocService);
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
  viewTaskSub$: Subscription;
  editTaskSub$: Subscription;

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

  private openDrawer(data: AdhocTaskDrawerData): void {
    this.dialog
      .open(AdhocTaskDrawerComponent, {
        ...dialogConfigHelper(this.overlay, data),
        // Right-anchored, full-height drawer (M5 plan decision) - see
        // adhoc-task-drawer.component.scss's `.adhoc-drawer-panel` rules
        // (ViewEncapsulation.None) for the panel-shape half of this.
        position: {top: '0', right: '0'},
        height: '100vh',
        maxHeight: '100vh',
        panelClass: 'adhoc-drawer-panel',
      })
      .afterClosed()
      .subscribe((result: AdhocTaskDrawerCloseResult) => {
        // The drawer's own "Udfør opgave" action (F8) closes with
        // `{action: 'complete', task}` instead of a plain boolean, so the
        // complete modal can be chained on top - satisfies the plan's
        // "triggered from the status cell on open rows AND from the
        // drawer" requirement without the drawer needing to know about
        // AdhocCompleteModalComponent itself.
        if (result && typeof result === 'object' && result.action === 'complete') {
          this.onCompleteTask(result.task);
          return;
        }
        if (result) {
          this.updateTable();
        }
      });
  }

  openCreateTask(): void {
    this.openDrawer({mode: 'create'});
  }

  // Refetch by id rather than reusing the table row: the row is a snapshot
  // from the last index call, and the drawer submits its photoIds back on
  // save, where ReconcilePhotosAsync soft-deletes by omission - so a stale
  // row silently wipes any photo added on mobile since the list was loaded.
  // Mirrors AdhocHistoryComponent.onRowClick, including its silent no-open
  // on a failed fetch.
  onViewTask(task: AdhocTaskModel): void {
    this.viewTaskSub$ = this.adhocService.getTask(task.id).subscribe((res) => {
      if (res && res.success && res.model) {
        this.openDrawer({mode: 'view', task: res.model});
      }
    });
  }

  onEditTask(task: AdhocTaskModel): void {
    this.editTaskSub$ = this.adhocService.getTask(task.id).subscribe((res) => {
      if (res && res.success && res.model) {
        this.openDrawer({mode: 'edit', task: res.model});
      }
    });
  }

  onCopyTask(task: AdhocTaskModel): void {
    this.dialog
      .open(AdhocCopyModalComponent, dialogConfigHelper(this.overlay, {id: task.id, title: task.title}))
      .afterClosed()
      .subscribe((result) => {
        if (result) {
          // The mockup's copy flow always lands the user in the edit
          // drawer on the freshly-created copy, not back on the table.
          this.openDrawer({mode: 'edit', task: result});
        }
      });
  }

  onDeleteTask(task: AdhocTaskModel): void {
    this.dialog
      .open(AdhocDeleteModalComponent, dialogConfigHelper(this.overlay, {id: task.id, title: task.title}))
      .afterClosed()
      .subscribe((result) => {
        if (result) {
          this.updateTable();
        }
      });
  }

  onCompleteTask(task: AdhocTaskModel): void {
    this.dialog
      .open(AdhocCompleteModalComponent, dialogConfigHelper(this.overlay, {id: task.id, propertyId: task.propertyId, title: task.title}))
      .afterClosed()
      .subscribe((result) => {
        if (result) {
          this.updateTable();
        }
      });
  }
}
