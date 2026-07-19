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

  // The two reassign selects exclude each other's chosen id so the same
  // worker can't be picked as both source and target. These MUST be stable
  // field references refreshed only on an actual selection change — NOT
  // getters computing `.filter()` on the fly: a getter bound to ng-select's
  // `[items]` returns a NEW array instance on every change-detection pass,
  // which makes ng-select rebuild and re-render its option list continuously
  // (each mousemove-triggered CD swaps the option DOM nodes), so pointer
  // interactions with the open dropdown never settle.
  fromWorkers: CommonDictionaryModel[] = [];
  toWorkers: CommonDictionaryModel[] = [];

  constructor(
    public dialogRef: MatDialogRef<BatchWorkerModalComponent>,
    @Inject(MAT_DIALOG_DATA) public data: TaskListBatchModalData,
    private taskListService: BackendConfigurationPnTaskListService,
  ) {
    this.refreshWorkerLists();
  }

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

  onFromSiteIdChange(id: number | null) {
    this.fromSiteId = id;
    this.refreshWorkerLists();
  }

  onToSiteIdChange(id: number | null) {
    this.toSiteId = id;
    this.refreshWorkerLists();
  }

  private refreshWorkerLists() {
    this.fromWorkers = (this.data.workers ?? []).filter(w => w.id !== this.toSiteId);
    this.toWorkers = (this.data.workers ?? []).filter(w => w.id !== this.fromSiteId);
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
