import {Component, Inject} from '@angular/core';
import {MAT_DIALOG_DATA, MatDialogRef} from '@angular/material/dialog';
import {BackendConfigurationPnTaskListService} from '../../../../../services';
import {TaskListBatchModalData} from '../../task-list-page/task-list-page.component';

/**
 * Batch "activate / deactivate" modal (#1123) — flips
 * `AreaRulePlanning.Status` (and, downstream, `Planning.Enabled`, which is what
 * the items-planning scheduler filters on) for every selected task.
 *
 * The two options reuse the single-task calendar modal's own status labels
 * verbatim (`Task visible on calendar` / `Task dimmed on calendar`); no new
 * option keys.
 *
 * Nothing is pre-selected, following `BatchComplianceModalComponent`'s
 * precedent for exactly the same reason: a radio pair has no "nothing chosen"
 * visual state, so a pre-checked option plus an always-enabled Save would let
 * an admin who only opened the modal to see what it offers write that value
 * onto every selected task — including the ones deliberately set the other way.
 * Save therefore stays disabled until an option is actively picked.
 *
 * The deactivate warning is shown ONLY when deactivating, because it is only
 * then that anything is destroyed, and it states what actually happens: the
 * open occurrences are pulled from the app, the completed ones and their
 * collected data are preserved. That is the honest version of wave 1's blanket
 * `'Collected data will not be deleted'` — which was true of the delete action's
 * SDK cases but says nothing about the occurrences that DO disappear.
 */
@Component({
  standalone: false,
  selector: 'app-batch-status-modal',
  templateUrl: './batch-status-modal.component.html',
})
export class BatchStatusModalComponent {
  // `null` until the admin picks one of the two options. See the class comment.
  active: boolean | null = null;

  constructor(
    public dialogRef: MatDialogRef<BatchStatusModalComponent>,
    @Inject(MAT_DIALOG_DATA) public data: TaskListBatchModalData,
    private taskListService: BackendConfigurationPnTaskListService,
  ) {}

  get valid(): boolean {
    return this.active != null;
  }

  hide() {
    this.dialogRef.close();
  }

  submit() {
    if (!this.valid) {
      return;
    }
    const taskIds = this.data.selectedTasks.map(t => t.id);
    this.taskListService
      // Non-null asserted: `valid` above is exactly the null guard, so the
      // payload stays strictly `boolean`.
      .setStatus({taskIds, active: this.active!})
      .subscribe(res => {
        if (res && res.success) {
          this.dialogRef.close(true);
        }
      });
  }
}
