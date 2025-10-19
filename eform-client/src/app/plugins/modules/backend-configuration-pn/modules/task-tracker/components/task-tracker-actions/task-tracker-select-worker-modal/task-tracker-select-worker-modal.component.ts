import {CommonDictionaryModel, WorkerModel} from 'src/app/common/models';
import {Component, EventEmitter, Inject, OnDestroy, OnInit, ViewChild} from '@angular/core';
import {MAT_DIALOG_DATA, MatDialogRef} from '@angular/material/dialog';
import {TaskWizardCreateModel} from 'src/app/plugins/modules/backend-configuration-pn/models';
import * as R from 'ramda';
import {TaskWizardStatusesEnum} from 'src/app/plugins/modules/backend-configuration-pn/enums';

@Component({
    selector: 'app-task-tracker-select-worker-modal',
    templateUrl: './task-tracker-select-worker-modal.component.html',
    styleUrls: ['./task-tracker-select-worker-modal.component.scss'],
    standalone: false
})
export class TaskTrackerSelectWorkerModalComponent implements OnInit, OnDestroy {
  sites: CommonDictionaryModel[] = [];
  workerName: string;
  workerSelected: EventEmitter<CommonDictionaryModel> = new EventEmitter<CommonDictionaryModel>();
  @ViewChild('modal') modal: any;
  workers: WorkerModel[] = [];
  public model: TaskWizardCreateModel = {
    eformId: null,
    folderId: null,
    propertyId: null,
    repeatEvery: null,
    repeatType: null,
    itemPlanningTagId: null,
    startDate: null,
    status: TaskWizardStatusesEnum.Active,
    sites: [],
    tagIds: [],
    translates: [],
    complianceEnabled: true
  };
  selectedSite: any;

  constructor(
    public dialogRef: MatDialogRef<TaskTrackerSelectWorkerModalComponent>,
    @Inject(MAT_DIALOG_DATA) workers: WorkerModel[]) {}

  ngOnDestroy(): void {
    }

  ngOnInit(): void {
    }

  show() {
    this.modal.show();
  }

  hide() {
    this.dialogRef.close();
  }

  create() {
    this.workerSelected.emit(this.selectedSite);
    this.hide();
  }

  fillModelAndCopyModel(model: TaskWizardCreateModel) {
    this.model = R.clone(model);
  }

}
