import {Component, EventEmitter, Inject, OnDestroy, OnInit, Output, ViewChild} from '@angular/core';
import {AutoUnsubscribe} from 'ngx-auto-unsubscribe';
import {TaskModel} from '../../../../models';
import {TaskTrackerStateService} from '../store';
import {
  BackendConfigurationPnPropertiesService,
  BackendConfigurationPnTaskManagementService
} from '../../../../services';
import {ToastrService} from 'ngx-toastr';
import {Subscription} from 'rxjs';
import {MAT_DIALOG_DATA, MatDialog, MatDialogRef} from '@angular/material/dialog';
import {TranslateService} from '@ngx-translate/core';
import {FormBuilder} from '@angular/forms';
import {IColumns} from 'src/app/plugins/modules/backend-configuration-pn/models/task-tracker/columns.model';
import {FormControl, FormGroup} from '@angular/forms';

@AutoUnsubscribe()
@Component({
  selector: 'app-task-tracker-shown-columns-container',
  templateUrl: './task-tracker-shown-columns.component.html',
  styleUrls: ['./task-tracker-shown-columns.component.scss'],
})
export class TaskTrackerShownColumnsComponent implements OnInit, OnDestroy {
  public dataChanged = new EventEmitter<IColumns>();
  constructor(
    @Inject(MAT_DIALOG_DATA) public data: IColumns,
    private translate: TranslateService,
    private _formBuilder: FormBuilder,
    private dialogRef: MatDialogRef<TaskTrackerShownColumnsComponent>
  ) {
  }

  columns: FormGroup = new FormGroup({
    property: new FormControl(this.data['Property']),
    task: new FormControl(this.data['Task']),
    tags: new FormControl(this.data['Tags']),
    workers: new FormControl(this.data['Workers']),
    start: new FormControl(this.data['Start']),
    repeat: new FormControl(this.data['Repeat']),
    deadline: new FormControl(this.data['Deadline'])
  })

  handleSave(value: IColumns) {
    /*if (this.currentPostRequestStatus){
      this.dialogRef.close(value)
    }*/
    this.dataChanged.emit(value);
  }
  hide() {
    this.dialogRef.close();
  }

  openDeleteModal(workOrderCaseModel: TaskModel) {
    /*const deleteModal = this.dialog.open(TaskManagementDeleteModalComponent, dialogConfigHelper(this.overlay, workOrderCaseModel));
    this.workOrderCaseDeleteSub$ = deleteModal.componentInstance.workOrderCaseDelete
      .subscribe(x => this.deleteWorkOrderCaseModel(x, deleteModal));*/
  }

  /*openCreateModal() {
    const createModal = this.dialog.open(TaskTrackerCreateShowModalComponent, dialogConfigHelper(this.overlay));
    this.taskCreatedSub$ = createModal.componentInstance.taskCreated.subscribe(() => this.updateTable());
  }*/

  ngOnInit(): void {
    /*this.getProperties();*/
    // eslint-disable-next-line no-console
    //console.log(this.data)
  }

  ngOnDestroy(): void {
  }
}
