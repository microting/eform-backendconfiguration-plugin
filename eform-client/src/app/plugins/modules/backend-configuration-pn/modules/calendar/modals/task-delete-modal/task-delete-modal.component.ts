import {Component, Inject} from '@angular/core';
import {MAT_DIALOG_DATA, MatDialogRef, MatDialogTitle, MatDialogContent, MatDialogActions} from '@angular/material/dialog';
import {BackendConfigurationPnCalendarService} from '../../../../services';
import {CalendarTaskModel} from '../../../../models/calendar';
import {TranslatePipe} from '@ngx-translate/core';

export interface TaskDeleteModalData {
  task: CalendarTaskModel;
}

@Component({
  selector: 'app-task-delete-modal',
  imports: [TranslatePipe, MatDialogTitle, MatDialogContent, MatDialogActions],
  templateUrl: './task-delete-modal.component.html',
})
export class TaskDeleteModalComponent {
  constructor(
    private dialogRef: MatDialogRef<TaskDeleteModalComponent>,
    @Inject(MAT_DIALOG_DATA) public data: TaskDeleteModalData,
    private calendarService: BackendConfigurationPnCalendarService,
  ) {}

  onConfirm() {
    this.calendarService.deleteTask(this.data.task.id, 'this', this.data.task.taskDate).subscribe(res => {
      if (res && res.success) this.dialogRef.close(true);
    });
  }

  onCancel() {
    this.dialogRef.close(null);
  }
}
