import {Component, EventEmitter, Inject, OnDestroy, OnInit, Optional, Output} from '@angular/core';
import {MAT_DIALOG_DATA, MatDialog, MatDialogRef} from '@angular/material/dialog';
import {Overlay} from '@angular/cdk/overlay';
import {Observable, Subscription} from 'rxjs';
import {dialogConfigHelper} from 'src/app/common/helpers';
import {
  BackendConfigurationPnCalendarService,
  BackendConfigurationPnCompliancesService,
} from '../../../../services';
import {CalendarBoardModel, CalendarTaskModel, RepeatDeleteScope} from '../../../../models/calendar';
import {CommonDictionaryModel, DataItemDto, ElementDto, ReplyElementDto} from 'src/app/common/models';
import {TaskDeleteModalComponent} from '../task-delete-modal/task-delete-modal.component';
import {RepeatScopeModalComponent} from '../repeat-scope-modal/repeat-scope-modal.component';
import {CalendarImageLightboxComponent} from '../calendar-image-lightbox/calendar-image-lightbox.component';
import {CalendarRepeatService} from '../../services/calendar-repeat.service';
import {getCurrentLocale} from '../../services/calendar-locale.helper';
import {TranslateService} from '@ngx-translate/core';

export interface TaskPreviewModalData {
  task: CalendarTaskModel;
  boards: CalendarBoardModel[];
  employees: CommonDictionaryModel[];
  tags: string[];
  properties: CommonDictionaryModel[];
  // eForm option list (id + label) for resolving the task's eformId to a
  // display name. Passed as the container's eforms$ observable — the
  // container calls loadEforms() before opening the preview.
  eforms: Observable<{id: number; label: string}[]>;
  // Item-planning tags (id + name) for resolving itemPlanningTagId to the
  // report headline ("Overskrift") name.
  planningTags: {id: number; name: string}[];
}

@Component({
  standalone: false,
  selector: 'app-task-preview-modal',
  templateUrl: './task-preview-modal.component.html',
  styleUrls: ['./task-preview-modal.component.scss'],
})
export class TaskPreviewModalComponent implements OnInit, OnDestroy {
  @Output() popoverClose = new EventEmitter<string | null>();
  usePopoverMode = false;

  private eformsList: {id: number; label: string}[] = [];

  // Picture-field thumbnails of the completed case (historical + completed
  // cards only). Loaded lazily per card-open — never cached across openings
  // (the component is recreated on every open).
  caseImageFileNames: string[] = [];
  caseImagesLoading = false;

  private subs: Subscription[] = [];

  constructor(
    @Optional() private dialogRef: MatDialogRef<TaskPreviewModalComponent>,
    @Optional() @Inject(MAT_DIALOG_DATA) public data: TaskPreviewModalData,
    private dialog: MatDialog,
    private overlay: Overlay,
    private calendarService: BackendConfigurationPnCalendarService,
    private compliancesService: BackendConfigurationPnCompliancesService,
    private repeatService: CalendarRepeatService,
    private translate: TranslateService,
  ) {}

  ngOnInit() {
    if (this.data.eforms) {
      this.subs.push(this.data.eforms.subscribe(list => (this.eformsList = list ?? [])));
    }
    if (this.isHistorical && this.data.task.completed && this.data.task.sdkCaseId) {
      this.loadCaseImages();
    }
  }

  ngOnDestroy() {
    this.subs.forEach(s => s.unsubscribe());
  }

  get boardName(): string {
    const board = this.data.boards.find(b => b.id === this.data.task.boardId);
    return board?.name ?? '';
  }

  get boardColor(): string {
    const board = this.data.boards.find(b => b.id === this.data.task.boardId);
    return board?.color ?? this.data.task.color;
  }

  get propertyName(): string {
    const prop = this.data.properties?.find(p => p.id === this.data.task.propertyId);
    return prop?.name ?? '';
  }

  get eformName(): string {
    const id = this.data.task.eformId;
    if (id == null) return '';
    return this.eformsList.find(e => e.id === id)?.label ?? '';
  }

  get reportHeadline(): string {
    const id = this.data.task.itemPlanningTagId;
    if (id == null) return '';
    return this.data.planningTags?.find(t => t.id === id)?.name ?? '';
  }

  /** Localized "Torsdag 16. juli 2026" — weekday first-letter capitalized. */
  get scheduleDateLabel(): string {
    const locale = getCurrentLocale(this.translate);
    const d = new Date(this.data.task.taskDate);
    const weekday = d.toLocaleDateString(locale, {weekday: 'long'});
    const rest = d.toLocaleDateString(locale, {day: 'numeric', month: 'long', year: 'numeric'});
    const label = `${weekday} ${rest}`;
    return label.charAt(0).toUpperCase() + label.slice(1);
  }

  get timeDisplay(): string {
    const task = this.data.task;
    const start = task.startText || this.hourToTimeStr(task.startHour);
    const end = task.endText || this.hourToTimeStr(task.startHour + task.duration);
    if (!start && !end) return '';
    return `${start} – ${end}`;
  }

  private hourToTimeStr(hour: number): string {
    if (hour == null || hour === 0) return '';
    const h = Math.floor(Math.min(hour, 23.75));
    const m = Math.round((hour % 1) * 60);
    return `${h.toString().padStart(2, '0')}:${m.toString().padStart(2, '0')}`;
  }

  /**
   * Full Gentag option label, matching what the edit modal's dropdown shows
   * for the task: reconstruct the repeat meta and format it via
   * CalendarRepeatService (e.g. "Weekly every torsdag", "Every 2 weeks: …").
   * Falls back to a generic rule label for legacy rows the reconstruction
   * can't recover.
   */
  get repeatLabel(): string {
    const task = this.data.task;
    if (!task.repeatRule || task.repeatRule === 'none') {
      return this.translate.instant('Does not repeat');
    }
    const meta = this.repeatService.reconstructMetaFromTask(task);
    if (meta) {
      return this.repeatService.formatCustomRepeatLabel(meta, getCurrentLocale(this.translate));
    }
    const labels: Record<string, string> = {
      daily: this.translate.instant('Daily'),
      weeklyOne: this.translate.instant('Weekly'),
      weeklyAll: this.translate.instant('Every weekday'),
      weekdays: this.translate.instant('All weekdays (Monday to Friday)'),
      monthlyDom: this.translate.instant('Monthly'),
      yearlyOne: this.translate.instant('Yearly'),
      custom: this.translate.instant('Custom'),
    };
    return labels[task.repeatRule] ?? task.repeatRule;
  }

  /**
   * Historical = completed (R2 immutability — regardless of date) OR the
   * task's end lies in the past. Mirrors the edit modal's isInPast rule
   * (taskDate + endTime vs. now).
   */
  get isHistorical(): boolean {
    const task = this.data.task;
    if (task.completed) return true;
    const endTime = task.endText || this.hourToTimeStr(task.startHour + task.duration) || '00:00';
    const [hours, minutes] = endTime.split(':').map(Number);
    const end = new Date(task.taskDate);
    end.setHours(hours, minutes, 0, 0);
    return end < new Date();
  }

  /** Non-null only when the group row should show "Completed by: <name>". */
  get completedByName(): string | null {
    const task = this.data.task;
    if (this.isHistorical && task.completed && task.doneByName) {
      return task.doneByName;
    }
    return null;
  }

  get assigneeChips(): string[] {
    const task = this.data.task;
    return [...(task.workerNames ?? []), ...(task.workerTagNames ?? [])];
  }

  imageSrc(fileName: string): string {
    return `/api/template-files/get-image/${fileName}`;
  }

  openLightbox(index: number) {
    this.dialog.open(CalendarImageLightboxComponent, {
      ...dialogConfigHelper(this.overlay, {
        images: this.caseImageFileNames,
        startIndex: index,
      }),
      // Lightbox must close on Esc + backdrop click (dialogConfigHelper
      // defaults to disableClose: true).
      disableClose: false,
      maxWidth: '95vw',
    });
  }

  private loadCaseImages() {
    this.caseImagesLoading = true;
    // templateId is unused server-side for the lookup; pass the task's
    // eformId when present (may be null on week-endpoint rows).
    this.subs.push(
      this.compliancesService
        .getCase(this.data.task.sdkCaseId!, this.data.task.eformId ?? 0)
        .subscribe({
          next: op => {
            this.caseImagesLoading = false;
            if (op && op.success && op.model) {
              this.caseImageFileNames = this.collectPictureFileNames(op.model);
            }
          },
          // Errors hide the row silently (caseImageFileNames stays empty).
          error: () => (this.caseImagesLoading = false),
        })
    );
  }

  /**
   * Walk the ReplyElement exactly like the complete-event modal's
   * partialLoadCase: recurse element lists + FieldContainers, collect the
   * Picture fieldValues' uploadedDataObj.fileName.
   */
  private collectPictureFileNames(reply: ReplyElementDto): string[] {
    const out: string[] = [];
    const walkDataItem = (item: DataItemDto) => {
      if (item.fieldType === 'FieldContainer') {
        (item.dataItemList ?? []).forEach(walkDataItem);
        return;
      }
      if (item.fieldType === 'Picture') {
        (item.fieldValues ?? []).forEach(v => {
          if (v.uploadedDataObj?.fileName) {
            out.push(v.uploadedDataObj.fileName);
          }
        });
      }
    };
    const walkElement = (el: ElementDto) => {
      (el.elementList ?? []).forEach(walkElement);
      (el.dataItemList ?? []).forEach(walkDataItem);
    };
    (reply.elementList ?? []).forEach(walkElement);
    return out;
  }

  onEdit() {
    this.close('edit');
  }

  onCopy() {
    this.close('copy');
  }

  onDelete() {
    const task = this.data.task;
    const isRepeating = !!task.repeatRule && task.repeatRule !== 'none';
    if (isRepeating) {
      const scopeRef = this.dialog.open(
        RepeatScopeModalComponent,
        dialogConfigHelper(this.overlay, {mode: 'delete'})
      );
      scopeRef.afterClosed().subscribe((scope: RepeatDeleteScope | null) => {
        if (!scope) return;
        this.calendarService
          .deleteTask(task.id, scope, task.taskDate)
          .subscribe(res => {
            if (res && res.success) this.close('reload');
          });
      });
      return;
    }
    const ref = this.dialog.open(
      TaskDeleteModalComponent,
      dialogConfigHelper(this.overlay, {task})
    );
    ref.afterClosed().subscribe(result => {
      if (result) this.close('reload');
    });
  }

  onClose() {
    this.close(null);
  }

  private close(result: string | null) {
    if (this.usePopoverMode) {
      this.popoverClose.emit(result);
    } else {
      this.dialogRef.close(result);
    }
  }
}
