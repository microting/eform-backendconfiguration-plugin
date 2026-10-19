import {Component, EventEmitter, OnDestroy, OnInit,
  inject
} from '@angular/core';
import {AutoUnsubscribe} from 'ngx-auto-unsubscribe';
import {MAT_DIALOG_DATA, MatDialogRef, MatDialogTitle, MatDialogContent, MatDialogActions} from '@angular/material/dialog';
import {TranslateService, TranslatePipe} from '@ngx-translate/core';
import {FormBuilder} from '@angular/forms';
import {Columns} from '../../../../../models';
import {FormControl, FormGroup, ReactiveFormsModule} from '@angular/forms';
import {MatCheckbox} from '@angular/material/checkbox';

@AutoUnsubscribe()
@Component({
    selector: 'app-task-tracker-shown-columns-container',
    imports: [ReactiveFormsModule, TranslatePipe, MatDialogTitle, MatDialogContent, MatDialogActions, MatCheckbox],
    templateUrl: './task-tracker-shown-columns.component.html',
    styleUrls: ['./task-tracker-shown-columns.component.scss'],
})
export class TaskTrackerShownColumnsComponent implements OnInit, OnDestroy {
  private data = inject<Columns>(MAT_DIALOG_DATA);
  private translate = inject(TranslateService);
  private _formBuilder = inject(FormBuilder);
  private dialogRef = inject(MatDialogRef<TaskTrackerShownColumnsComponent>);

  public columnsChanged = new EventEmitter<Columns>();
  columns: FormGroup;

  
  constructor() {
    this.columns = new FormGroup({
      property: new FormControl(this.data['property']),
      task: new FormControl(this.data['task']),
      tags: new FormControl(this.data['tags']),
      workers: new FormControl(this.data['workers']),
      start: new FormControl(this.data['start']),
      repeat: new FormControl(this.data['repeat']),
      deadline: new FormControl(this.data['deadline']),
      calendar: new FormControl(this.data['calendar']),
    });
  }


  save() {
    this.columnsChanged.emit(this.columns.value);
  }

  hide() {
    this.dialogRef.close();
  }

  setColumns(columns: Columns) {
    this.columns.patchValue({
      property: columns['property'],
      task: columns['task'],
      tags: columns['tags'],
      workers: columns['workers'],
      start: columns['start'],
      repeat: columns['repeat'],
      deadline: columns['deadline'],
      calendar: columns['calendar'],
    });
  }

  ngOnInit(): void {
  }

  ngOnDestroy(): void {
  }
}
