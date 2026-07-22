import {Component, Inject} from '@angular/core';
import {MAT_DIALOG_DATA, MatDialogRef} from '@angular/material/dialog';
import {BackendConfigurationPnTaskListService} from '../../../../../services';
import {TaskListBatchModalData} from '../../task-list-page/task-list-page.component';

// Covers both addTags and removeTags — same multi-select shape, different
// vocabulary (data.tags is the full tag list for add, the union of tags
// already present on the selected rows for remove — the page precomputes
// this) and a different service call on submit.
@Component({
  standalone: false,
  selector: 'app-batch-tags-modal',
  templateUrl: './batch-tags-modal.component.html',
})
export class BatchTagsModalComponent {
  tagIds: number[] = [];

  constructor(
    public dialogRef: MatDialogRef<BatchTagsModalComponent>,
    @Inject(MAT_DIALOG_DATA) public data: TaskListBatchModalData,
    private taskListService: BackendConfigurationPnTaskListService,
  ) {}

  get isRemove(): boolean {
    return this.data.mode === 'removeTags';
  }

  get title(): string {
    return this.isRemove ? 'Remove tags' : 'Add tags';
  }

  get valid(): boolean {
    return this.tagIds.length > 0;
  }

  hide() {
    this.dialogRef.close();
  }

  submit() {
    if (!this.valid) {
      return;
    }
    const taskIds = this.data.selectedTasks.map(t => t.id);
    const call = this.isRemove
      ? this.taskListService.removeTags({taskIds, tagIds: this.tagIds})
      : this.taskListService.addTags({taskIds, tagIds: this.tagIds});
    call.subscribe(res => {
      if (res && res.success) {
        this.dialogRef.close(true);
      }
    });
  }
}
