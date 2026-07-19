import {Component, Inject} from '@angular/core';
import {MAT_DIALOG_DATA, MatDialogRef} from '@angular/material/dialog';
import {CommonDictionaryModel} from 'src/app/common/models';
import {BackendConfigurationPnTaskListService} from '../../../../../services';
import {TaskListBatchModalData} from '../../task-list-page/task-list-page.component';

// Covers three batch actions that all boil down to "pick worker(s)": assign
// (move all selected tasks to one worker), addWorker (add one worker to the
// selected tasks' existing assignees) and reassign (move from one worker to
// another). The template swaps between a single select and a from/to pair
// based on data.mode.
@Component({
  standalone: false,
  selector: 'app-batch-worker-modal',
  templateUrl: './batch-worker-modal.component.html',
})
export class BatchWorkerModalComponent {
  siteId: number | null = null;      // assign + addWorker
  fromSiteId: number | null = null;  // reassign
  toSiteId: number | null = null;    // reassign

  constructor(
    public dialogRef: MatDialogRef<BatchWorkerModalComponent>,
    @Inject(MAT_DIALOG_DATA) public data: TaskListBatchModalData,
    private taskListService: BackendConfigurationPnTaskListService,
  ) {}

  get isReassign(): boolean {
    return this.data.mode === 'reassign';
  }

  get title(): string {
    switch (this.data.mode) {
      case 'assign':
        return 'Move selected to employee';
      case 'addWorker':
        return 'Add employee';
      case 'reassign':
      default:
        return 'Move from employee to employee';
    }
  }

  // The two reassign selects exclude each other's chosen id so the same
  // worker can't be picked as both source and target.
  get fromWorkers(): CommonDictionaryModel[] {
    return (this.data.workers ?? []).filter(w => w.id !== this.toSiteId);
  }

  get toWorkers(): CommonDictionaryModel[] {
    return (this.data.workers ?? []).filter(w => w.id !== this.fromSiteId);
  }

  get valid(): boolean {
    return this.isReassign
      ? this.fromSiteId != null && this.toSiteId != null && this.fromSiteId !== this.toSiteId
      : this.siteId != null;
  }

  hide() {
    this.dialogRef.close();
  }

  submit() {
    if (!this.valid) {
      return;
    }
    const taskIds = this.data.selectedTasks.map(t => t.id);
    const call = this.data.mode === 'assign'
      ? this.taskListService.assign({taskIds, siteId: this.siteId!})
      : this.data.mode === 'addWorker'
        ? this.taskListService.addWorker({taskIds, siteId: this.siteId!})
        : this.taskListService.reassign({taskIds, fromSiteId: this.fromSiteId!, toSiteId: this.toSiteId!});
    call.subscribe(res => {
      if (res && res.success) {
        this.dialogRef.close(true);
      }
    });
  }
}
