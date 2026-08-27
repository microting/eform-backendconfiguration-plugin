import {Component, Inject} from '@angular/core';
import {MAT_DIALOG_DATA, MatDialogRef} from '@angular/material/dialog';
import {BackendConfigurationPnTaskListService} from '../../../../../services';
import {TaskListBatchModalData} from '../../task-list-page/task-list-page.component';

/**
 * Batch "set compliance" modal — flips `ComplianceEnabled` on every selected
 * task, i.e. whether an overdue occurrence is moved into the property's
 * "00. Overdue tasks" folder by the nightly job (and therefore shown in the
 * app) or left alone.
 *
 * The two options reuse the single-task calendar modal's own toggle labels
 * verbatim, which are the user's own wording; no new option keys.
 *
 * Unlike the calendar modal — whose `onPickOverdueShown`/`onPickOverdueHidden`
 * both force Status active — this batch action never touches Status. Flipping
 * compliance on a large selection must not silently reactivate dormant tasks;
 * batch activation is a separate action.
 *
 * Nothing is pre-selected. A radio pair has no "nothing chosen" visual state,
 * so a pre-checked option would let an admin who only opened the modal to see
 * what it offers hit Save and silently write that value onto every selected
 * task — including the ones deliberately set the other way. Save therefore
 * stays disabled until an option is actively picked, matching the sibling
 * batch modals' `get valid()` / `[disabled]="!valid"` idiom.
 */
@Component({
  standalone: false,
  selector: 'app-batch-compliance-modal',
  templateUrl: './batch-compliance-modal.component.html',
})
export class BatchComplianceModalComponent {
  // `null` until the admin picks one of the two options — deliberately NOT
  // defaulted to the value a newly created task carries. See the class comment.
  complianceEnabled: boolean | null = null;

  constructor(
    public dialogRef: MatDialogRef<BatchComplianceModalComponent>,
    @Inject(MAT_DIALOG_DATA) public data: TaskListBatchModalData,
    private taskListService: BackendConfigurationPnTaskListService,
  ) {}

  get valid(): boolean {
    return this.complianceEnabled != null;
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
      .setCompliance({taskIds, complianceEnabled: this.complianceEnabled!})
      .subscribe(res => {
        if (res && res.success) {
          this.dialogRef.close(true);
        }
      });
  }
}
