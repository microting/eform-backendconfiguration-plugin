import {Component, EventEmitter, Inject, Optional, Output} from '@angular/core';
import {MAT_DIALOG_DATA, MatDialog, MatDialogRef} from '@angular/material/dialog';
import {Overlay} from '@angular/cdk/overlay';
import {dialogConfigHelper} from 'src/app/common/helpers';
import {BackendConfigurationPnCalendarService} from '../../../../services';
import {CalendarBoardModel, CalendarTaskModel} from '../../../../models/calendar';
import {CommonDictionaryModel} from 'src/app/common/models';
import {TaskDeleteModalComponent} from '../task-delete-modal/task-delete-modal.component';

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

  get repeatLabel(): string {
    const rule = this.data.task.repeatRule;
    const labels: Record<string, string> = {
      daily: 'Daily',
      weekly: 'Weekly',
      weekdays: 'Every weekday',
      monthly: 'Monthly',
      yearly: 'Yearly',
      custom: 'Custom',
    };
    return labels[rule] ?? rule;
  }

  onEdit() {
    this.close('edit');
  }

  onDelete() {
    const ref = this.dialog.open(
      TaskDeleteModalComponent,
      dialogConfigHelper(this.overlay, {
        task: this.data.task,
        hasSeries: !!this.data.task.repeatSeriesId,
      })
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
