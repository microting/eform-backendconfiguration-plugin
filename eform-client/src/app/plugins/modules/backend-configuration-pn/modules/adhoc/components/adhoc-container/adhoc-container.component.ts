import {Component, OnDestroy, OnInit, inject} from '@angular/core';
import {AutoUnsubscribe} from 'ngx-auto-unsubscribe';
import {ActivatedRoute, Router} from '@angular/router';
import {AppMenuStateService} from 'src/app/common/store';
import {Subscription} from 'rxjs';

/**
 * Top bar for the "Adhoc overblik" dashboard (M5/F4): page title (resolved
 * from the nav menu's translation for this route, matching
 * TaskManagementContainerComponent's pattern) + the Overblik/Historik
 * segment toggle + "Ny opgave" button.
 *
 * This is a skeleton only - the table view (F5), toolbar filters (F6), task
 * drawer (F7) and history tab + modals (F8) are separate follow-up tasks.
 * The Historik segment link's target route and the "Ny opgave" button's
 * dialog are wired up once those land; until then this component alone
 * fills the page.
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

  // Resolved once in ngOnInit — getTitleByUrl subscribes to the menu store
  // internally without unsubscribing, so calling it from the template would
  // leak one subscription per change-detection tick (same rationale as
  // TaskManagementContainerComponent).
  public pageTitle: string = '';

  titleSub$: Subscription;

  ngOnInit(): void {
    this.titleSub$ = this.appMenuStateService.leftAppMenus$.subscribe(() => {
      this.pageTitle = this.appMenuStateService.getTitleByUrl(this.router.url);
    });
  }

  ngOnDestroy(): void {
  }

  get isHistoryView(): boolean {
    return this.route.snapshot.firstChild?.routeConfig?.path === 'history';
  }

  // TODO(F7): open the "Ny opgave" drawer (MatDialog, create mode) once
  // AdhocTaskDrawerComponent exists.
  openCreateTask(): void {
  }
}
