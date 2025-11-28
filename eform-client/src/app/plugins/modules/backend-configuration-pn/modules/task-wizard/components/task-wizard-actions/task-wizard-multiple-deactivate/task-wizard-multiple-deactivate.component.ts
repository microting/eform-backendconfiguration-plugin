import {Component, EventEmitter, OnInit,
  inject
} from '@angular/core';
import {MAT_DIALOG_DATA, MatDialogRef} from '@angular/material/dialog';

@Component({
    selector: 'app-task-wizard-multiple-deactivate',
    templateUrl: './task-wizard-multiple-deactivate.component.html',
    styleUrls: ['./task-wizard-multiple-deactivate.component.scss'],
    standalone: false
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
