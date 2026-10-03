import {
  Component, Inject, OnInit, QueryList, ViewChildren, inject,
} from '@angular/core';
import {MAT_DATE_FORMATS} from '@angular/material/core';
import {MAT_DIALOG_DATA, MatDialogRef} from '@angular/material/dialog';
import {CALENDAR_MAT_DATE_FORMATS} from '../../calendar-date-formats';
import {EFormService} from 'src/app/common/services';
import {
  TemplateDto, CaseEditRequest, ReplyElementDto, ReplyRequest,
  ElementDto, DataItemDto, CommonDictionaryModel,
} from 'src/app/common/models';
import {CaseEditElementComponent} from 'src/app/common/modules/eform-cases/components';
import {
  BackendConfigurationPnCasesService,
  BackendConfigurationPnCompliancesService,
  BackendConfigurationPnCalendarService,
  BackendConfigurationPnPropertiesService,
} from '../../../../services';
import {CalendarPrepareCompleteResult} from '../../../../models';
import {endOfDay, isAfter, parseISO} from 'date-fns';
import * as R from 'ramda';

/**
 * #1373 — what the dialog needs to EDIT an already completed log instead of completing
 * an occurrence: the existing case and template, and who completed it. Rapport opens the
 * dialog this way, so a log is edited in the same dialog Detaljer completes it in.
 */
export interface CalendarCompleteEventEditData {
  sdkCaseId: number;
  checkListId: number;
  /** The SDK site that completed the case — kept as the completer unless changed. */
  completedBySiteId: number | null;
  /** That worker's name, listed even when they are no longer linked to the property. */
  completedByName?: string;
}

export interface CalendarCompleteEventModalData {
  taskId: number;
  complianceId: number | null;
  occurrenceDate: string;
  propertyId: number;
  /**
   * The event's EXPLICIT individual assignees. Grouping uses this together with
   * `teamAssigneeIds`; the pre-select uses this one ALONE (see `applyPreselect`).
   */
  assigneeIds: number[];
  /**
   * The sites assigned to the event via a worker tag ("team") — the live members of
   * its worker tags (#1236). Optional: a caller that has no team information omits
   * it and the modal behaves exactly as it did before the field existed.
   */
  teamAssigneeIds?: number[];
  /**
   * The TASK's name, used as the dialog title (#1205). Optional so a caller
   * that omits it degrades to the generic 'Complete task' fallback — never to
   * the embedded eForm template's name, which is what the header used to show.
   */
  taskTitle?: string;
  /**
   * `'compliance'` when a compliance page (Detaljer) opens the modal (#1300):
   * forwarded to `prepare-complete` and `cases/calendar` so the server refuses
   * to complete a task dated after today. The calendar omits it and keeps its
   * intended early completion.
   */
  source?: 'compliance';
  /**
   * Edit mode (#1373): skip `prepare-complete`, load the existing case with its answers,
   * keep its done date and completer, and save through `PUT .../cases`
   * (`BackendConfigurationCaseService.Update`), which updates the case rather than
   * completing a new one. Absent → the ordinary completion flow.
   */
  edit?: CalendarCompleteEventEditData;
}

@Component({
  selector: 'app-calendar-complete-event-modal',
  templateUrl: './calendar-complete-event-modal.component.html',
  styleUrls: ['./calendar-complete-event-modal.component.scss'],
  standalone: false,
  // "Udført dato" renders the long Danish form. Declared HERE, not only on
  // CalendarModule, because MatDialog creates this component under the injector
  // of the module that opened it (`Dialog._createInjector` parents on
  // `config.injector ?? config.viewContainerRef?.injector ?? MatDialog._injector`,
  // and MatDialogModule provides MatDialog per-module). A sibling module that
  // opens this modal — the standalone Compliance page, #1165 — therefore must
  // NOT be forced to carry CalendarModule's MAT_DATE_FORMATS just to keep this
  // input's format. A component provider is on the node-injector path of both
  // the `matInput [matDatepicker]` and the popup calendar (MatDatepicker
  // attaches MatDatepickerContent through its own ViewContainerRef), so both
  // resolve it whatever the environment injector says.
  providers: [{provide: MAT_DATE_FORMATS, useValue: CALENDAR_MAT_DATE_FORMATS}],
})
export class CalendarCompleteEventModalComponent implements OnInit {
  private dialogRef = inject(MatDialogRef<CalendarCompleteEventModalComponent>);
  private compliancesService = inject(BackendConfigurationPnCompliancesService);
  private calendarService = inject(BackendConfigurationPnCalendarService);
  private casesService = inject(BackendConfigurationPnCasesService);
  private propertiesService = inject(BackendConfigurationPnPropertiesService);
  private eFormService = inject(EFormService);

  @ViewChildren(CaseEditElementComponent)
  editElements: QueryList<CaseEditElementComponent>;

  sites: CommonDictionaryModel[] = [];
  /**
   * `sites` with a `group` discriminator so mtx-select can render the workers
   * assigned to this event above everyone else. Rebuilt whenever `sites` is set
   * rather than computed in a getter, because a getter would hand ng-select a
   * new array identity on every change-detection pass and livelock the panel.
   */
  groupedSites: Array<CommonDictionaryModel & {group?: string}> = [];
  selectedWorkerId: number | null = null;

  prepared: CalendarPrepareCompleteResult | null = null;
  currenteForm: TemplateDto = new TemplateDto();
  replyElement: ReplyElementDto = new ReplyElementDto();
  maxDate = new Date();
  loading = true;
  isSaving = false;
  /** Edit mode: the done date the case was loaded with, to tell whether it was changed. */
  private loadedDoneAt: Date | null = null;

  constructor(@Inject(MAT_DIALOG_DATA) public data: CalendarCompleteEventModalData) {}

  ngOnInit() {
    // Edit mode keeps the log's completer: set before the workers arrive, so the
    // pre-select below (which never overrides a choice) leaves it alone.
    const edit = this.data.edit;
    if (edit) {
      this.selectedWorkerId = edit.completedBySiteId ?? null;
    }
    // Workers and prepare run in parallel; the case loads once prepare returns.
    this.propertiesService.getLinkedSites(this.data.propertyId, false).subscribe(res => {
      if (!res?.success || !res.model) { return; }
      this.sites = [...res.model].sort((a, b) => a.name.localeCompare(b.name, 'da'));
      this.listCompleter();
      this.buildGroupedSites();
      this.applyPreselect();
    });
    if (edit) {
      // Nothing to prepare: the case exists and is completed.
      this.loadTemplateInfo();
      return;
    }
    this.calendarService
      .prepareComplete(this.data.taskId, this.data.complianceId, this.data.occurrenceDate, this.data.source)
      .subscribe({
        next: res => {
          if (!res?.success || !res.model) { this.dialogRef.close({saved: false}); return; }
          this.prepared = res.model;
          this.applyPreselect();
          this.loadTemplateInfo();
        },
        error: () => this.dialogRef.close({saved: false}),
      });
  }

  // Preselect: the event's single assigned worker when there is exactly one,
  // else the site the case is deployed to — but only when that site is in the
  // property-workers list. Multi-assignee events stay unselected (explicit pick).
  //
  // `teamAssigneeIds` never CAUSES a pre-select — the decision recorded in #1236:
  // worker-tag members are grouped as assigned by buildGroupedSites, but they never
  // pre-select. What this control sets is the site recorded as having completed the
  // case (`replyElement.siteId` in saveCase), so a team of one must not have a name
  // chosen on the user's behalf. Do not "make the two consistent" by counting team
  // members here. The one place it is read suppresses the deployed-site fallback for
  // a team-only event (#1352).
  private applyPreselect() {
    if (this.selectedWorkerId != null || this.sites.length === 0) { return; }
    if (this.data.assigneeIds?.length === 1
        && this.sites.some(s => s.id === this.data.assigneeIds[0])) {
      this.selectedWorkerId = this.data.assigneeIds[0];
      return;
    }
    if (this.data.assigneeIds?.length > 1) { return; }
    // Team-only event (#1352): the case lives on a team member, not on who did the work.
    if (!this.data.assigneeIds?.length && this.data.teamAssigneeIds?.length) { return; }
    const assigned = this.prepared?.assignedSiteId;
    if (assigned != null && this.sites.some(s => s.id === assigned)) {
      this.selectedWorkerId = assigned;
    }
  }

  /**
   * Edit mode: the worker who completed the log stays selectable even when they are no
   * longer linked to the property (resigned, moved), so saving does not silently drop or
   * change the completer.
   */
  private listCompleter() {
    const id = this.data.edit?.completedBySiteId;
    if (id == null || this.sites.some(s => s.id === id)) { return; }
    this.sites = [
      ...this.sites,
      {id, name: this.data.edit.completedByName || `#${id}`, description: ''},
    ];
  }

  /**
   * Split the worker list into "assigned to this event" and everyone else.
   * When the split would leave a group empty — no assignees, or every worker
   * assigned — the list stays ungrouped rather than showing a header with
   * nothing under it.
   *
   * "Assigned" is the UNION of the explicit individual assignees and the members of
   * any worker tag ("team") the event is assigned to (#1236). Reconciliation deploys
   * the case to a team's members, so they are shown where the people who do the work
   * belong. The Set de-dupes a site that is both. The pre-select in `applyPreselect`
   * never picks a team member and is intentionally NOT the same rule.
   */
  private buildGroupedSites() {
    const assigned = new Set([
      ...(this.data.assigneeIds ?? []),
      ...(this.data.teamAssigneeIds ?? []),
    ]);
    const inGroup = this.sites.filter(s => assigned.has(s.id));
    const rest = this.sites.filter(s => !assigned.has(s.id));

    if (inGroup.length === 0 || rest.length === 0) {
      this.groupedSites = [...this.sites];
      return;
    }

    // Stable keys, not translated strings: `instant()` here would freeze the
    // headers at worker-load time and render raw keys forever if the locale
    // bundle had not resolved yet. The template translates them at render time.
    this.groupedSites = [
      ...inGroup.map(s => ({...s, group: 'assigned'})),
      ...rest.map(s => ({...s, group: 'other'})),
    ];
  }

  /**
   * The dialog opens at the single-section width because the section count is
   * not known until the case has loaded. A multi-section eForm also renders a
   * nav column, so it needs the extra room — widen once, after load, rather
   * than opening wide and centring a narrow column inside it (which left the
   * title flush at 16px while the fields sat at 96px).
   */
  private widenForSections() {
    if (!this.hasMultipleSections) { return; }
    this.dialogRef.updateSize('min(90vw, 1080px)');
  }

  get hasMultipleSections(): boolean {
    return (this.replyElement?.elementList?.length ?? 0) > 1;
  }

  /**
   * Section headings only earn their vertical space when there is more than one
   * section to tell apart. A single-section eForm gets one heading above one
   * undivided list of fields, which adds a row of chrome and no information, so
   * it is suppressed. (Until #1205 this was justified by the heading repeating
   * the dialog title; the dialog is now titled with the TASK name, so the two
   * strings differ — the suppression stands on density alone.)
   */
  get showSectionTitles(): boolean {
    return this.hasMultipleSections;
  }

  get canSave(): boolean {
    return !this.isSaving && !this.loading
      && this.selectedWorkerId != null && !!this.replyElement.doneAt
      && !this.doneAtAfterToday;
  }

  /**
   * #1373 — a done date may not lie after today: a completed log is placed on its done
   * date everywhere. The datepicker's `max` stops a pick; this also stops a stored future
   * date (an early completion from before the rule) from being saved again unchanged.
   */
  private get doneAtAfterToday(): boolean {
    const doneAt = this.toDate(this.replyElement.doneAt);
    return !!doneAt && isAfter(doneAt, endOfDay(new Date()));
  }

  /** The case being completed or edited. */
  get caseId(): number | null {
    return this.data.edit?.sdkCaseId ?? this.prepared?.sdkCaseId ?? null;
  }

  private loadTemplateInfo() {
    const templateId = this.data.edit?.checkListId ?? this.prepared?.templateId;
    if (!templateId) { this.dialogRef.close({saved: false}); return; }
    this.eFormService.getSingle(templateId).subscribe(op => {
      if (op && op.success) {
        this.currenteForm = op.model;
        this.loadCase();
      } else {
        this.dialogRef.close({saved: false});
      }
    });
  }

  private loadCase() {
    const id = this.caseId;
    if (!id) { this.dialogRef.close({saved: false}); return; }
    this.compliancesService.getCase(id, this.currenteForm.id).subscribe(op => {
      if (op && op.success) {
        this.replyElement = op.model;
        if (this.data.edit) {
          // Keep the log's own done date.
          this.loadedDoneAt = this.replyElement.doneAt ?? null;
        } else {
          const defaultDoneAt =
            this.toDate(this.prepared?.eventStart) ?? this.toDate(this.prepared?.deadline) ?? new Date();
          // Completing a future occurrence early: clamp the default into the
          // datepicker's allowed range (max = today) so the pre-filled value is valid.
          this.replyElement.doneAt = defaultDoneAt > this.maxDate ? new Date() : defaultDoneAt;
        }
        this.loading = false;
        this.widenForSections();
      } else {
        this.dialogRef.close({saved: false});
      }
    });
  }

  private toDate(value: string | Date | undefined | null): Date | null {
    if (value == null) { return null; }
    if (value instanceof Date) { return value; }
    if (typeof value === 'string' && value.length > 0) { return parseISO(value); }
    return null;
  }

  saveCase() {
    if (!this.canSave || (!this.prepared && !this.data.edit)) { return; }
    const requestModels: Array<CaseEditRequest> = [];
    this.editElements.forEach(x => {
      x.extractData();
      requestModels.push(x.requestModel);
    });
    const replyRequest = new ReplyRequest();
    replyRequest.id = this.caseId;
    replyRequest.label = this.replyElement.label;
    replyRequest.elementList = requestModels;
    replyRequest.doneAt = this.replyElement.doneAt;
    replyRequest.extraId = this.prepared?.complianceId ?? this.data.complianceId;
    replyRequest.siteId = this.selectedWorkerId;
    this.isSaving = true;
    const save$ = this.data.edit
      ? this.casesService.updateCase(this.editRequest(replyRequest), this.currenteForm.id)
      : this.compliancesService.updateCaseFromCalendar(replyRequest, this.currenteForm.id, this.data.source);
    save$.subscribe({
      next: op => {
        this.isSaving = false;
        if (op && op.success) { this.dialogRef.close({saved: true}); }
      },
      error: () => { this.isSaving = false; },
    });
  }

  /**
   * Edit mode's done date, in the shape `BackendConfigurationCaseService.Update` reads
   * (the legacy case page's contract): a PICKED day is sent as that day's UTC midnight
   * and the server keeps the case's time of day; an unchanged date is sent back as the
   * stored instant, so nothing shifts.
   */
  private editRequest(replyRequest: ReplyRequest): ReplyRequest {
    const doneAt = this.toDate(this.replyElement.doneAt);
    if (doneAt && doneAt.getTime() !== this.toDate(this.loadedDoneAt)?.getTime()) {
      replyRequest.doneAt = new Date(Date.UTC(doneAt.getFullYear(), doneAt.getMonth(), doneAt.getDate()));
    }
    return replyRequest;
  }

  cancel() {
    this.dialogRef.close({saved: false});
  }

  goToSection(location: string): void {
    setTimeout(() => {
      const target = document.querySelector(location) as HTMLElement | null;
      target?.parentElement?.scrollIntoView({behavior: 'smooth'});
    });
  }

  partialLoadCase() {
    const id = this.caseId;
    if (!id) { return; }
    this.compliancesService.getCase(id, this.currenteForm.id).subscribe(op => {
      if (op && op.success) {
        const fn = (pathForLens: Array<number | string>) => {
          const lens = R.lensPath(pathForLens);
          let dataItem: (ElementDto | DataItemDto) = R.view(lens, op.model);
          // @ts-ignore
          if (dataItem.elementList !== undefined || dataItem.dataItemList !== undefined) {
            dataItem = dataItem as ElementDto;
            if (dataItem.elementList) {
              for (let i = 0; i < dataItem.elementList.length; i++) {
                fn([...pathForLens, 'elementList', i]);
              }
            }
            if (dataItem.dataItemList) {
              for (let i = 0; i < dataItem.dataItemList.length; i++) {
                fn([...pathForLens, 'dataItemList', i]);
              }
            }
          } else { // @ts-ignore
            if (dataItem.fieldType !== undefined) {
              dataItem = dataItem as DataItemDto;
              if (dataItem.fieldType === 'FieldContainer') {
                for (let i = 0; i < dataItem.dataItemList.length; i++) {
                  fn([...pathForLens, 'dataItemList', i]);
                }
              }
              if (dataItem.fieldType === 'Picture') {
                this.replyElement = R.set(lens, dataItem, this.replyElement);
              }
            }
          }
        };
        for (let i = 0; i < op.model.elementList.length; i++) {
          fn(['elementList', i]);
        }
      }
    });
  }
}
