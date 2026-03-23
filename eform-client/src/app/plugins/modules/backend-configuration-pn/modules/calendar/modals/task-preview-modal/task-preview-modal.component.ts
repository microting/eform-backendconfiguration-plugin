import {Component, Inject} from '@angular/core';
import {MAT_DIALOG_DATA, MatDialog, MatDialogRef} from '@angular/material/dialog';
import {Overlay} from '@angular/cdk/overlay';
import {dialogConfigHelper} from 'src/app/common/helpers';
import {BackendConfigurationPnCalendarService} from '../../../../services';
import {CalendarBoardModel, CalendarTaskModel} from '../../../../models/calendar';
import {CommonDictionaryModel} from 'src/app/common/models';
import {TaskCreateEditModalComponent} from '../task-create-edit-modal/task-create-edit-modal.component';
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
})
export class TaskPreviewModalComponent {
  constructor(
    private dialogRef: MatDialogRef<TaskPreviewModalComponent>,
    @Inject(MAT_DIALOG_DATA) public data: TaskPreviewModalData,
    private dialog: MatDialog,
    private overlay: Overlay,
    private calendarService: BackendConfigurationPnCalendarService,
  ) {}

  get boardName(): string {
    const board = this.data.boards.find(b => b.id === this.data.task.boardId);
    return board?.name ?? '';
  }

  onEdit() {
    const ref = this.dialog.open(
      TaskCreateEditModalComponent,
      dialogConfigHelper(this.overlay, {
        task: this.data.task,
        date: this.data.task.taskDate,
        startHour: this.data.task.startHour,
        boards: this.data.boards,
        employees: this.data.employees ?? [],
        tags: this.data.tags ?? this.data.task.tags ?? [],
        propertyId: this.data.task.propertyId,
        properties: this.data.properties ?? [],
      })
    );
    ref.afterClosed().subscribe(result => {
      if (result) this.dialogRef.close('reload');
    });
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
      if (result) this.dialogRef.close('reload');
    });
  }

  onClose() {
    this.dialogRef.close(null);
  }
}
