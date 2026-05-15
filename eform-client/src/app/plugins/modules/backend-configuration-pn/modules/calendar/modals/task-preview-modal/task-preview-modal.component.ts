import {Component, EventEmitter, Inject, Optional, Output} from '@angular/core';
import {MAT_DIALOG_DATA, MatDialog, MatDialogRef} from '@angular/material/dialog';
import {Overlay} from '@angular/cdk/overlay';
import {dialogConfigHelper} from 'src/app/common/helpers';
import {BackendConfigurationPnCalendarService} from '../../../../services';
import {CalendarBoardModel, CalendarTaskModel} from '../../../../models/calendar';
import {CommonDictionaryModel} from 'src/app/common/models';
import {TaskDeleteModalComponent} from '../task-delete-modal/task-delete-modal.component';
import {RepeatScopeModalComponent} from '../repeat-scope-modal/repeat-scope-modal.component';
import {RepeatDeleteScope} from '../../../../models/calendar';
import {TranslateService} from '@ngx-translate/core';

export interface TaskPreviewModalData {
  task: CalendarTaskModel;
  boards: CalendarBoardModel[];
  employees: CommonDictionaryModel[];
  tags: string[];
  properties: CommonDictionaryModel[];
}

@Component({
  standalone: false,
  selector: 'app-task-preview-modal',
  templateUrl: './task-preview-modal.component.html',
  styleUrls: ['./task-preview-modal.component.scss'],
})
export class TaskPreviewModalComponent {
  @Output() popoverClose = new EventEmitter<string | null>();
  usePopoverMode = false;

  constructor(
    @Optional() private dialogRef: MatDialogRef<TaskPreviewModalComponent>,
    @Optional() @Inject(MAT_DIALOG_DATA) public data: TaskPreviewModalData,
    private dialog: MatDialog,
    private overlay: Overlay,
    private calendarService: BackendConfigurationPnCalendarService,
    private translate: TranslateService,
  ) {}

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

  get repeatLabel(): string {
    const rule = this.data.task.repeatRule;
    const labels: Record<string, string> = {
      daily: this.translate.instant('Daily'),
      weeklyOne: this.translate.instant('Weekly'),
      weeklyAll: this.translate.instant('Every weekday'),
      monthlyDom: this.translate.instant('Monthly'),
      yearlyOne: this.translate.instant('Yearly'),
      custom: this.translate.instant('Custom'),
    };
    return labels[rule] ?? rule;
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
