import {
  Component, Inject, OnInit, QueryList, ViewChildren, inject,
} from '@angular/core';
import {MAT_DIALOG_DATA, MatDialogRef} from '@angular/material/dialog';
import {TranslateService} from '@ngx-translate/core';
import {EFormService} from 'src/app/common/services';
import {
  TemplateDto, CaseEditRequest, ReplyElementDto, ReplyRequest,
  ElementDto, DataItemDto, CommonDictionaryModel,
} from 'src/app/common/models';
import {CaseEditElementComponent} from 'src/app/common/modules/eform-cases/components';
import {
  BackendConfigurationPnCompliancesService,
  BackendConfigurationPnCalendarService,
  BackendConfigurationPnPropertiesService,
} from '../../../../services';
import {CalendarPrepareCompleteResult} from '../../../../models';
import {parseISO} from 'date-fns';
import * as R from 'ramda';

export interface CalendarCompleteEventModalData {
  taskId: number;
  complianceId: number | null;
  occurrenceDate: string;
  propertyId: number;
  assigneeIds: number[];
}

@Component({
  selector: 'app-calendar-complete-event-modal',
  templateUrl: './calendar-complete-event-modal.component.html',
  styleUrls: ['./calendar-complete-event-modal.component.scss'],
  standalone: false,
})
export class CalendarCompleteEventModalComponent implements OnInit {
  private dialogRef = inject(MatDialogRef<CalendarCompleteEventModalComponent>);
  private compliancesService = inject(BackendConfigurationPnCompliancesService);
  private calendarService = inject(BackendConfigurationPnCalendarService);
  private propertiesService = inject(BackendConfigurationPnPropertiesService);
  private eFormService = inject(EFormService);
  private translateService = inject(TranslateService);

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

  constructor(@Inject(MAT_DIALOG_DATA) public data: CalendarCompleteEventModalData) {}

  ngOnInit() {
    // Workers and prepare run in parallel; the case loads once prepare returns.
    this.propertiesService.getLinkedSites(this.data.propertyId, false).subscribe(res => {
      if (!res?.success || !res.model) { return; }
      this.sites = [...res.model].sort((a, b) => a.name.localeCompare(b.name, 'da'));
      this.buildGroupedSites();
      this.applyPreselect();
    });
    this.calendarService
      .prepareComplete(this.data.taskId, this.data.complianceId, this.data.occurrenceDate)
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
  private applyPreselect() {
    if (this.selectedWorkerId != null || this.sites.length === 0) { return; }
    if (this.data.assigneeIds?.length === 1
        && this.sites.some(s => s.id === this.data.assigneeIds[0])) {
      this.selectedWorkerId = this.data.assigneeIds[0];
      return;
    }
    if (this.data.assigneeIds?.length > 1) { return; }
    const assigned = this.prepared?.assignedSiteId;
    if (assigned != null && this.sites.some(s => s.id === assigned)) {
      this.selectedWorkerId = assigned;
    }
  }

  /**
   * Split the worker list into "assigned to this event" and everyone else.
   * When the split would leave a group empty — no assignees, or every worker
   * assigned — the list stays ungrouped rather than showing a header with
   * nothing under it.
   */
  private buildGroupedSites() {
    const assigned = new Set(this.data.assigneeIds ?? []);
    const inGroup = this.sites.filter(s => assigned.has(s.id));
    const rest = this.sites.filter(s => !assigned.has(s.id));

    if (inGroup.length === 0 || rest.length === 0) {
      this.groupedSites = [...this.sites];
      return;
    }

    this.groupedSites = [
      ...inGroup.map(s => ({...s, group: this.translateService.instant('Assigned workers')})),
      ...rest.map(s => ({...s, group: this.translateService.instant('Other workers')})),
    ];
  }

  get hasMultipleSections(): boolean {
    return (this.replyElement?.elementList?.length ?? 0) > 1;
  }

  /**
   * A single-section eForm names its one section the same thing the dialog is
   * already titled, so printing both repeats the name two rows apart.
   */
  get showSectionTitles(): boolean {
    return this.hasMultipleSections;
  }

  get canSave(): boolean {
    return !this.isSaving && !this.loading
      && this.selectedWorkerId != null && !!this.replyElement.doneAt;
  }

  private loadTemplateInfo() {
    const templateId = this.prepared?.templateId;
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
    const id = this.prepared?.sdkCaseId;
    if (!id) { this.dialogRef.close({saved: false}); return; }
    this.compliancesService.getCase(id, this.currenteForm.id).subscribe(op => {
      if (op && op.success) {
        this.replyElement = op.model;
        const defaultDoneAt =
          this.toDate(this.prepared?.eventStart) ?? this.toDate(this.prepared?.deadline) ?? new Date();
        // Completing a future occurrence early: clamp the default into the
        // datepicker's allowed range (max = today) so the pre-filled value is valid.
        this.replyElement.doneAt = defaultDoneAt > this.maxDate ? new Date() : defaultDoneAt;
        this.loading = false;
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
    if (!this.canSave || !this.prepared) { return; }
    const requestModels: Array<CaseEditRequest> = [];
    this.editElements.forEach(x => {
      x.extractData();
      requestModels.push(x.requestModel);
    });
    const replyRequest = new ReplyRequest();
    replyRequest.id = this.prepared.sdkCaseId;
    replyRequest.label = this.replyElement.label;
    replyRequest.elementList = requestModels;
    replyRequest.doneAt = this.replyElement.doneAt;
    replyRequest.extraId = this.prepared.complianceId;
    replyRequest.siteId = this.selectedWorkerId;
    this.isSaving = true;
    this.compliancesService.updateCaseFromCalendar(replyRequest, this.currenteForm.id).subscribe({
      next: op => {
        this.isSaving = false;
        if (op && op.success) { this.dialogRef.close({saved: true}); }
      },
      error: () => { this.isSaving = false; },
    });
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
    const id = this.prepared?.sdkCaseId;
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
