import {Overlay, OverlayRef} from '@angular/cdk/overlay';
import {TemplatePortal} from '@angular/cdk/portal';
import {
  Component,
  ElementRef,
  NgZone,
  OnDestroy,
  OnInit,
  TemplateRef,
  ViewChild,
  ViewContainerRef,
} from '@angular/core';
import {MatDialog} from '@angular/material/dialog';
import {TranslateService} from '@ngx-translate/core';
import {Subject, Subscription, merge, of} from 'rxjs';
import {catchError, finalize, switchMap, takeUntil, tap} from 'rxjs/operators';
import {ComplianceReportPagedModel, ComplianceReportRowModel} from '../../../../models';
import {
  BackendConfigurationPnComplianceReportService,
  BackendConfigurationPnCompliancesService,
} from '../../../../services';
import {getCurrentLocale} from '../../../calendar/services/calendar-locale.helper';
import {
  CalendarCompleteEventModalComponent,
  CalendarCompleteEventModalData,
} from '../../../calendar/modals/calendar-complete-event-modal/calendar-complete-event-modal.component';
import {
  COMPLIANCE_EMPTY_CELL,
  ComplianceWeekGroup,
  formatComplianceDayLabel,
  formatComplianceTimeRange,
  groupRowsByWeek,
} from '../../helpers';
import {ComplianceReportStateService} from '../../store';

/** How long the post-completion ring stays on the row (compliance.css:266). */
const HIGHLIGHT_MS = 2200;

/**
 * The Detaljer view of the standalone Compliance page (#1165): the
 * week-grouped chronological task log.
 *
 * It draws rows only. The filter bar, the mode toggle, the spinner and the
 * pagination chrome all belong to the shell (#1163); this component's whole
 * contract with it is:
 *
 *  - subscribe to `fetchRequested$` — the ONLY fetch trigger. It replays its
 *    last emission to a late subscriber on purpose, which is what makes a mode
 *    switch (an `ngSwitch` that destroys this component and creates the next)
 *    actually query;
 *  - read `requestModel` AT FETCH TIME, never cached;
 *  - report `setTotalCount()` so the shell can draw the page buttons, and
 *    `setLoading()` so it can disable `Opdater tabel`.
 *
 * Rows arrive already ordered `taskDate` DESC, `startHour` ASC (#1161 owns
 * that); nothing here re-sorts.
 *
 * KNOWN STRUCTURAL LIMITATION: the row element is `role="button"`
 * `tabindex="0"` and contains the delete `<button>`, i.e. interactive content
 * nested inside a `role="button"` — invalid ARIA. Splitting the activation
 * target from the actions cell means re-cutting the row's CSS grid plus the
 * hover/highlight rules keyed off `.compliance-details__row`, which is beyond
 * #1165; the concrete defect it caused (Enter/Space on the delete button also
 * opening the eForm) is fixed by the guards in `onRowClicked` and on the
 * actions cell.
 */
@Component({
  standalone: false,
  selector: 'app-compliance-details-view',
  templateUrl: './compliance-details-view.component.html',
  styleUrls: ['./compliance-details-view.component.scss'],
})
export class ComplianceDetailsViewComponent implements OnInit, OnDestroy {
  @ViewChild('deleteConfirmTpl') deleteConfirmTpl!: TemplateRef<unknown>;

  readonly emptyCell = COMPLIANCE_EMPTY_CELL;

  groups: ComplianceWeekGroup[] = [];
  /** Rows on the CURRENT page. `state.total` is the whole filtered set. */
  rowCount = 0;
  hasFetched = false;
  /**
   * A fetch failed while there was NOTHING on screen to keep — i.e. the first
   * fetch of this visit. It drives the `Kunne ikke indlæse data` line; without
   * it that case renders an entirely blank card (`reportVisible` true so the
   * shell's placeholder is gone, `loading` false so the spinner is gone,
   * `hasFetched` false so the "no tasks match" line is suppressed, and no
   * rows). Never set for a RE-fetch: there the previous rendering stays.
   */
  loadFailed = false;
  /** The row wearing the post-completion ring, if any. */
  highlightedId: number | null = null;

  private destroy$ = new Subject<void>();
  /** Refreshes that are NOT a user gesture: after a delete or a completion. */
  private refresh$ = new Subject<void>();
  private deleteOverlayRef: OverlayRef | null = null;
  private deleteAnchor: HTMLElement | null = null;
  private outsideClickSub: Subscription | null = null;
  private overlayKeydownSub: Subscription | null = null;
  private pendingDeleteId: number | null = null;
  /** Anchor whose own click already closed the popover; see openDeleteConfirm. */
  private suppressReopenFor: HTMLElement | null = null;
  /** Set before the completion modal opens; consumed by the next response. */
  private pendingHighlightId: number | null = null;
  private highlightTimer: ReturnType<typeof setTimeout> | null = null;
  private scrollTimer: ReturnType<typeof setTimeout> | null = null;
  /** Previous paging position, so only a PAGE change scrolls the container. */
  private lastPage = 0;
  private lastShowAll = false;

  constructor(
    public state: ComplianceReportStateService,
    private complianceReportService: BackendConfigurationPnComplianceReportService,
    private compliancesService: BackendConfigurationPnCompliancesService,
    private translate: TranslateService,
    private dialog: MatDialog,
    private overlay: Overlay,
    private viewContainerRef: ViewContainerRef,
    private host: ElementRef<HTMLElement>,
    private zone: NgZone,
  ) {}

  ngOnInit(): void {
    this.lastPage = this.state.page;
    this.lastShowAll = this.state.showAll;

    merge(this.state.fetchRequested$, this.refresh$)
      .pipe(
        tap(() => {
          // Every re-render closes the delete popover first: it is positioned
          // against a button that is about to be detached, and an orphaned
          // popover pointing at a dead node is a real bug (#1165).
          this.closeDeletePopover();
          // Cleared on every attempt: while the spinner is up the previous
          // failure is no longer the current state of the view. It is re-set
          // below if this attempt fails too.
          this.loadFailed = false;
          this.state.setLoading(true);
        }),
        // switchMap, so a page click landing while an earlier request is in
        // flight cancels it rather than racing it into the view.
        switchMap(() =>
          this.complianceReportService.index(this.state.requestModel).pipe(
            // The service already toasts a failed OperationResult; swallow the
            // transport error here so the trigger stream survives it.
            catchError(() => of(null)),
          ),
        ),
        // Runs on unsubscribe too — i.e. when takeUntil completes the stream on
        // destroy — so a request still in flight when the ngSwitch tears this
        // component down cannot leave `loading` stuck true and `Opdater tabel`
        // permanently disabled.
        finalize(() => this.state.setLoading(false)),
        takeUntil(this.destroy$),
      )
      .subscribe((res) => {
        this.state.setLoading(false);
        if (!res || !res.success) {
          // The service has already toasted the failure. For a RE-fetch, leave
          // the previous rendering standing rather than replacing it with "no
          // tasks match the selected filters", which would blame the filters
          // for what was a transport or server error.
          //
          // On the FIRST fetch of the visit there is no previous rendering to
          // keep, and every other thing that could occupy the card is gated
          // off (shell placeholder by `reportVisible`, spinner by `loading`,
          // empty line by `hasFetched`) — so the user would be left with a
          // blank card and a toast that may already have gone. Say what
          // happened instead, in its own wording: reusing the empty-result
          // text would blame the filters.
          this.loadFailed = !this.hasFetched;
          return;
        }
        this.applyResponse(res.model ?? null);
      });
  }

  ngOnDestroy(): void {
    this.closeDeletePopover();
    this.clearTimers();
    // No `setLoading(false)` here on purpose — not because of any ordering
    // between the outgoing and the incoming child (there is none to rely on:
    // `NgSwitchCase.ngDoCheck` only CREATES the incoming view, and the child's
    // `ngOnInit` — hence its own `setLoading(true)` — does not run until
    // `refreshEmbeddedViews`, which is AFTER every `ngDoCheck` in the host
    // view; this `ngOnDestroy` always precedes it, in either switch
    // direction). The reason is ownership: `loading` is the SHELL's flag. The
    // shell resets it in `setMode()`, `setFilter()` and `enterPage()`, which
    // covers every transition that unmounts this component, and for the
    // ordinary teardown the `finalize` above already clears it (it sits
    // UPSTREAM of `takeUntil`, so completing the stream here unsubscribes
    // through it and fires the callback). A second reset here would be
    // redundant with that `finalize` on exactly the same teardown.
    this.destroy$.next();
    this.destroy$.complete();
  }

  // -------------------------------------------------------------------
  // Rendering
  // -------------------------------------------------------------------

  private applyResponse(model: ComplianceReportPagedModel | null): void {
    const total = model?.total ?? 0;
    const entities = model?.entities ?? [];
    this.state.setTotalCount(total);
    this.hasFetched = true;
    this.rowCount = entities.length;
    this.groups = groupRowsByWeek(
      entities,
      getCurrentLocale(this.translate),
      this.translate.instant('Week'),
    );

    // A delete can empty the page the user is standing on (the last row of the
    // last page). Re-clamp rather than showing an empty page with buttons that
    // say there are results. `setPage` re-fires the trigger, and it terminates
    // because the index strictly decreases.
    if (!this.state.showAll && entities.length === 0 && total > 0 && this.state.page > 0) {
      this.state.setPage(Math.min(this.state.page - 1, this.state.totalPages - 1));
      return;
    }

    const pagingChanged = this.state.page !== this.lastPage || this.state.showAll !== this.lastShowAll;
    this.lastPage = this.state.page;
    this.lastShowAll = this.state.showAll;

    // One frame later: the rows above are rendered on the next change-detection
    // pass, so neither the scroll target nor the highlighted row exists yet.
    this.clearTimer('scroll');
    this.scrollTimer = setTimeout(() => {
      if (pagingChanged) {
        this.scrollResultsIntoView();
      }
      this.applyPendingHighlight();
    });
  }

  private scrollResultsIntoView(): void {
    // The shell owns the result container (#1163) and gives it this id.
    document
      .getElementById('complianceCasesRoot')
      ?.scrollIntoView({behavior: 'smooth', block: 'start'});
  }

  /**
   * Land the user back on the row they just completed: scroll it to the middle
   * of the viewport and ring it for 2.2 s, so "it went green" is visible.
   *
   * With server-side paging the client holds one page, so the row is located
   * within the page that was just re-fetched. Completion changes neither
   * `taskDate` nor `startHour`, and Detaljer does not re-sort, so a row that is
   * still in the filtered set keeps its ordinal and therefore its page. Under
   * the default `Ikke udførte opgaver` status it leaves the set altogether —
   * there is nothing to highlight, and nothing is scrolled.
   */
  private applyPendingHighlight(): void {
    const id = this.pendingHighlightId;
    this.pendingHighlightId = null;
    if (id == null) {
      return;
    }
    const el = this.host.nativeElement.querySelector<HTMLElement>(
      `[data-compliance-id="${id}"]`,
    );
    if (!el) {
      return;
    }
    el.scrollIntoView({behavior: 'smooth', block: 'center'});
    this.highlightedId = id;
    this.clearTimer('highlight');
    // Outside Angular: a 2.2 s timer that only removes a CSS class does not
    // need to schedule a whole application tick while it waits.
    this.zone.runOutsideAngular(() => {
      this.highlightTimer = setTimeout(() => {
        this.zone.run(() => {
          this.highlightedId = null;
          this.highlightTimer = null;
        });
      }, HIGHLIGHT_MS);
    });
  }

  isRowCompletable(row: ComplianceReportRowModel): boolean {
    return !row.completed && row.areaRulePlanningId != null;
  }

  formatDayLabel(taskDate: string): string {
    return formatComplianceDayLabel(taskDate, getCurrentLocale(this.translate));
  }

  formatTimeRange(row: ComplianceReportRowModel): string {
    return formatComplianceTimeRange(row);
  }

  /**
   * Index-prefixed: `groupRowsByWeek` opens a block on every CHANGE of week,
   * so two blocks on one page can only share a key if the rows were not
   * contiguous by week — which a sort other than the default would produce.
   * Angular throws on a duplicate trackBy key, so the index keeps that a
   * rendering quirk rather than a crash.
   */
  trackByGroup(index: number, group: ComplianceWeekGroup): string {
    return `${index}:${group.key}`;
  }

  trackByRow(_: number, row: ComplianceReportRowModel): number {
    return row.complianceId;
  }

  // -------------------------------------------------------------------
  // Row click → completion
  // -------------------------------------------------------------------

  /**
   * Repackages the compliance row into the calendar's task shape and opens the
   * existing completion pipeline (`CalendarCompleteEventModalComponent` →
   * `prepare-complete` → the case editor), byte for byte as
   * `calendar-container.component.ts:778-787` and `:796+` do it.
   *
   * `assigneeIds: []` is deliberate and is the current behaviour: a compliance
   * row carries `workerNames`, not ids, so the modal cannot pre-select the
   * worker the way the calendar grid can. Adding `workerSiteIds` to the row DTO
   * is the clean fix and belongs on the backend paging issue, not here.
   */
  onRowClicked(row: ComplianceReportRowModel, event?: Event): void {
    if (!this.isRowCompletable(row)) {
      return;
    }
    // The actions cell holds a real <button> nested inside this role="button"
    // row (see the note on `openDeleteConfirm`), so a KEYBOARD activation of
    // the delete button bubbles here as well: Enter fires keydown on the row
    // and then the browser's default activation clicks the button, opening the
    // eForm modal AND the delete popover behind it. The actions cell stops
    // both click and keydown, and this second guard makes the row handler
    // independent of that: anything originating inside the actions area is not
    // a row activation.
    const target = event?.target as HTMLElement | null;
    if (target?.closest?.('.compliance-details__actions')) {
      return;
    }
    const ref = this.dialog.open(CalendarCompleteEventModalComponent, {
      data: {
        taskId: row.areaRulePlanningId ?? 0,
        complianceId: row.complianceId,
        occurrenceDate: row.taskDate,
        propertyId: row.propertyId,
        assigneeIds: [],
      } as CalendarCompleteEventModalData,
      // Sized for a single-section eForm; the modal widens itself when the form
      // turns out to have more than one section.
      width: 'min(90vw, 900px)',
      maxWidth: '95vw',
      autoFocus: false,
      restoreFocus: false,
    });
    ref
      .afterClosed()
      .pipe(takeUntil(this.destroy$))
      .subscribe((result?: {saved?: boolean}) => {
        // Always reload — even on cancel — so any partial state re-renders from
        // the canonical server view (another user may have touched the row in
        // the meantime). Do not "optimise" this into a saved-only reload
        // (#1165).
        //
        // The HIGHLIGHT, though, is saved-only: ringing the row after a CANCEL
        // announces a change that never happened, and under the default
        // `Ikke udførte opgaver` status it was the only case that ever rang at
        // all — a saved row leaves that result set, so there is nothing left in
        // the DOM to scroll to. No highlight is the correct outcome there;
        // `applyPendingHighlight` already no-ops when the row is absent.
        if (result?.saved) {
          this.pendingHighlightId = row.complianceId;
        }
        this.refresh$.next();
      });
  }

  // -------------------------------------------------------------------
  // Delete — anchored confirm popover
  // -------------------------------------------------------------------

  /**
   * Opens the confirm popover to the LEFT of its button, vertically centred,
   * 8 px away, clamped 8 px inside the viewport and repositioned on resize
   * (CDK's `FlexibleConnectedPositionStrategy` re-applies on a viewport change,
   * which is what removes the prototype's hand-rolled clamp maths).
   *
   * `stopPropagation` keeps the click off the row handler, so the delete button
   * never opens the eForm. The popover itself is attached to the CDK overlay
   * container at body level, NOT inside the row, so its own clicks cannot
   * bubble into the row either.
   */
  openDeleteConfirm(row: ComplianceReportRowModel, event: MouseEvent): void {
    event.stopPropagation();
    const anchor = event.currentTarget as HTMLElement;
    // Second click on the SAME button closes it and stops there. The close
    // itself has already happened — CDK's outside-click dispatcher listens on
    // `body` in the CAPTURE phase, so it runs before this handler — and it
    // leaves this marker behind precisely so the click does not reopen what it
    // just closed. A click on a DIFFERENT row's button leaves no marker, so it
    // moves the popover, which is the prototype's behaviour
    // (bindDeleteButtons, compliance.js:1319-1329).
    if (this.suppressReopenFor === anchor) {
      this.suppressReopenFor = null;
      return;
    }
    this.suppressReopenFor = null;
    this.closeDeletePopover();

    this.pendingDeleteId = row.complianceId;
    this.deleteAnchor = anchor;

    const positionStrategy = this.overlay
      .position()
      .flexibleConnectedTo(anchor)
      .withPositions([
        {originX: 'start', originY: 'center', overlayX: 'end', overlayY: 'center', offsetX: -8},
        // Fallback for a button hard against the left edge.
        {originX: 'end', originY: 'center', overlayX: 'start', overlayY: 'center', offsetX: 8},
      ])
      .withPush(true)
      .withViewportMargin(8);

    this.deleteOverlayRef = this.overlay.create({
      positionStrategy,
      scrollStrategy: this.overlay.scrollStrategies.reposition(),
      // No backdrop on purpose: a backdrop would swallow the click that is
      // meant to move the popover to another row's button, which the prototype
      // supports (bindDeleteButtons, compliance.js:1319).
      hasBackdrop: false,
      width: '280px',
    });
    this.deleteOverlayRef.attach(new TemplatePortal(this.deleteConfirmTpl, this.viewContainerRef));

    // Escape closes it. CDK's keyboard dispatcher routes keydown to the
    // TOP-MOST overlay with subscribers, so this works whether focus sits in
    // the popover or is still on the trigger button.
    this.overlayKeydownSub = this.deleteOverlayRef.keydownEvents().subscribe((keyEvent) => {
      if (keyEvent.key === 'Escape') {
        keyEvent.stopPropagation();
        this.closeDeletePopover();
      }
    });

    // Deliberately NOT `aria-modal` and NOT focus-trapped: nothing outside is
    // inert, and a trap would break the one gesture the popover exists to
    // support — clicking ANOTHER row's delete button to move it (there is no
    // backdrop for the same reason). Instead focus simply moves to `Annuller`
    // on open and is put back on the trigger on close, so a keyboard user can
    // reach both actions and never loses their place.
    // `attach` renders the embedded view synchronously (DomPortalOutlet calls
    // detectChanges), so the button is in the DOM by now.
    this.deleteOverlayRef.overlayElement
      .querySelector<HTMLElement>('.compliance-delete-popover__cancel')
      ?.focus();

    // Any click outside the popover closes it (the dispatcher already re-enters
    // the zone). `stopPropagation` cannot suppress it: the dispatcher listens
    // on `body` with `capture: true`, so it sees every click first — including
    // clicks on the delete buttons themselves, which is what the reopen marker
    // above exists to handle.
    this.outsideClickSub = this.deleteOverlayRef.outsidePointerEvents().subscribe((outside) => {
      const target = outside.target as Node | null;
      const closingAnchor = this.deleteAnchor;
      this.closeDeletePopover();
      // ONLY for the `click`. CDK's OverlayOutsideClickDispatcher listens for
      // four event types on `body` — pointerdown, click, auxclick and
      // contextmenu — and the marker exists solely to swallow the `click` that
      // this handler's own close has already answered. A right-click
      // (contextmenu) or a middle-click (auxclick) on the open popover's button
      // is NOT followed by a click on it, so a marker set from those would
      // survive and eat the user's next real left click.
      if (outside.type === 'click' && target && closingAnchor && closingAnchor.contains(target)) {
        this.suppressReopenFor = closingAnchor;
      }
    });
  }

  closeDeletePopover(): void {
    // Restore focus to the button that opened the popover, but ONLY when focus
    // is actually inside it — a close triggered by clicking elsewhere (or by a
    // re-render) must not yank focus back to a row action the user has left.
    // `isConnected` covers the re-render case, where the anchor node is already
    // detached.
    const overlayElement = this.deleteOverlayRef?.overlayElement ?? null;
    const closingAnchor = this.deleteAnchor;
    const focusWasInside =
      !!overlayElement && !!document.activeElement && overlayElement.contains(document.activeElement);

    this.overlayKeydownSub?.unsubscribe();
    this.overlayKeydownSub = null;
    this.outsideClickSub?.unsubscribe();
    this.outsideClickSub = null;
    this.deleteOverlayRef?.dispose();
    this.deleteOverlayRef = null;
    this.deleteAnchor = null;
    if (focusWasInside && closingAnchor?.isConnected) {
      closingAnchor.focus();
    }
    this.pendingDeleteId = null;
    // Cleared here, and re-set by the outside-click handler AFTER it calls
    // this. Left standing, a marker from a closed-by-re-render popover would
    // swallow the next click on a delete button whose DOM node trackBy reused.
    this.suppressReopenFor = null;
  }

  cancelDelete(): void {
    this.closeDeletePopover();
  }

  /**
   * Deletes the COMPLIANCE LOG ROW through the existing endpoint
   * (`DELETE api/backend-configuration-pn/compliances/delete/{id}`) and nothing
   * else. That endpoint is shared with the standalone `/compliances` table, so
   * neither it nor `deleteCompliance()` is touched here — this is a new caller
   * of an unchanged method.
   */
  confirmDelete(): void {
    const id = this.pendingDeleteId;
    this.closeDeletePopover();
    if (id == null) {
      return;
    }
    this.compliancesService
      .deleteCompliance(id)
      .pipe(takeUntil(this.destroy$))
      .subscribe((res) => {
        if (res?.success) {
          this.refresh$.next();
        }
      });
  }

  // -------------------------------------------------------------------

  private clearTimers(): void {
    this.clearTimer('highlight');
    this.clearTimer('scroll');
  }

  private clearTimer(which: 'highlight' | 'scroll'): void {
    if (which === 'highlight' && this.highlightTimer !== null) {
      clearTimeout(this.highlightTimer);
      this.highlightTimer = null;
    }
    if (which === 'scroll' && this.scrollTimer !== null) {
      clearTimeout(this.scrollTimer);
      this.scrollTimer = null;
    }
  }
}
