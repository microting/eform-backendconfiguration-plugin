import {Component, Inject} from '@angular/core';
import {MAT_DIALOG_DATA, MatDialogRef, MatDialogTitle, MatDialogContent, MatDialogActions} from '@angular/material/dialog';
import {BackendConfigurationPnTaskListService} from '../../../../../services';
import {TaskListBatchModalData} from '../../task-list-page/task-list-page.component';
import {NgFor} from '@angular/common';
import {TranslatePipe} from '@ngx-translate/core';

@Component({
  selector: 'app-batch-delete-modal',
  imports: [NgFor, TranslatePipe, MatDialogTitle, MatDialogContent, MatDialogActions],
  templateUrl: './batch-delete-modal.component.html',
})
export class BatchDeleteModalComponent {
  constructor(
    public dialogRef: MatDialogRef<BatchDeleteModalComponent>,
    @Inject(MAT_DIALOG_DATA) public data: TaskListBatchModalData,
    private taskListService: BackendConfigurationPnTaskListService,
  ) {}

  hide() {
    this.dialogRef.close();
  }

  submit() {
    const taskIds = this.data.selectedTasks.map(t => t.id);
    this.taskListService.delete({taskIds}).subscribe(res => {
      if (res && res.success) {
        this.dialogRef.close(true);
      }
    });
  }
}
