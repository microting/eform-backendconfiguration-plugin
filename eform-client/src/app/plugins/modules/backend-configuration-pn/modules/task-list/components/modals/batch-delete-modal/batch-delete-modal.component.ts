import {Component, Inject} from '@angular/core';
import {MAT_DIALOG_DATA, MatDialogRef} from '@angular/material/dialog';
import {BackendConfigurationPnTaskListService} from '../../../../../services';
import {TaskListBatchModalData} from '../../task-list-page/task-list-page.component';

@Component({
  standalone: false,
  selector: 'app-batch-delete-modal',
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
