import {Component, EventEmitter, OnInit,
  inject
} from '@angular/core';
import {MAT_DIALOG_DATA, MatDialogRef, MatDialogTitle, MatDialogActions} from '@angular/material/dialog';
import {TranslatePipe} from '@ngx-translate/core';

@Component({
    selector: 'app-task-wizard-multiple-deactivate',
    imports: [TranslatePipe, MatDialogTitle, MatDialogActions],
    templateUrl: './task-wizard-multiple-deactivate.component.html',
    styleUrls: ['./task-wizard-multiple-deactivate.component.scss'],
})
export class TaskWizardMultipleDeactivateComponent implements OnInit {
  public dialogRef = inject(MatDialogRef<TaskWizardMultipleDeactivateComponent>);
  public selectedTaskCount = inject<number>(MAT_DIALOG_DATA);

  deactivateMultipleTasks: EventEmitter<void> = new EventEmitter<void>();
  

  ngOnInit() {}

  hide() {
    this.dialogRef.close();
  }

  onDeactivateMultipleTasks() {
    this.deactivateMultipleTasks.emit();
  }
}
