import {Component, Inject} from '@angular/core';
import {MAT_DIALOG_DATA, MatDialogRef, MatDialogTitle, MatDialogContent, MatDialogActions} from '@angular/material/dialog';
import {BackendConfigurationPnTaskListService} from '../../../../../services';
import {TaskListBatchModalData} from '../../task-list-page/task-list-page.component';
import {NgIf, NgFor} from '@angular/common';
import {FormsModule} from '@angular/forms';
import {TranslatePipe} from '@ngx-translate/core';
import {MatFormField, MatLabel} from '@angular/material/form-field';
import {MtxSelect} from '@ng-matero/extensions/select';

// Two-phase: pick the target eForm, then explicitly confirm — changing the
// eForm on already-deployed tasks takes effect immediately, so the confirm
// step exists purely to stop an accidental click.
@Component({
  selector: 'app-batch-eform-modal',
  imports: [NgIf, NgFor, FormsModule, TranslatePipe, MatFormField, MatLabel, MtxSelect, MatDialogTitle, MatDialogContent, MatDialogActions],
  templateUrl: './batch-eform-modal.component.html',
})
export class BatchEformModalComponent {
  eformId: number | null = null;
  confirming = false;

  constructor(
    public dialogRef: MatDialogRef<BatchEformModalComponent>,
    @Inject(MAT_DIALOG_DATA) public data: TaskListBatchModalData,
    private taskListService: BackendConfigurationPnTaskListService,
  ) {}

  get valid(): boolean {
    return this.eformId != null;
  }

  hide() {
    this.dialogRef.close();
  }

  submit() {
    if (!this.valid) {
      return;
    }
    this.confirming = true;
  }

  confirm() {
    const taskIds = this.data.selectedTasks.map(t => t.id);
    this.taskListService.changeEform({taskIds, eformId: this.eformId!}).subscribe(res => {
      if (res && res.success) {
        this.dialogRef.close(true);
      }
    });
  }
}
