import {Component, EventEmitter, Inject, OnInit,} from '@angular/core';
import {MAT_DIALOG_DATA, MatDialogRef} from '@angular/material/dialog';

@Component({
  selector: 'app-task-wizard-multiple-deactivate',
  templateUrl: './task-wizard-multiple-deactivate.component.html',
  styleUrls: ['./task-wizard-multiple-deactivate.component.scss'],
})
export class TaskWizardMultipleDeactivateComponent implements OnInit {
  deactivateMultipleTasks: EventEmitter<void> = new EventEmitter<void>();
  constructor(
    public dialogRef: MatDialogRef<TaskWizardMultipleDeactivateComponent>,
    @Inject(MAT_DIALOG_DATA) public selectedTaskCount: number,
  ) {}

  ngOnInit() {}

  hide() {
    this.dialogRef.close();
  }

  onDeactivateMultipleTasks() {
    this.deactivateMultipleTasks.emit();
  }
}
