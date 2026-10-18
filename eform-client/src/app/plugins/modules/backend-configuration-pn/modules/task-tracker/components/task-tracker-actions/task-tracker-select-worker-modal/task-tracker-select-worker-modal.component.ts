import {CommonDictionaryModel, WorkerModel} from 'src/app/common/models';
import {Component, EventEmitter, OnDestroy, OnInit, ViewChild,
  inject
} from '@angular/core';
import {MAT_DIALOG_DATA, MatDialogRef, MatDialogTitle, MatDialogContent, MatDialogActions} from '@angular/material/dialog';
import {TaskWizardCreateModel} from 'src/app/plugins/modules/backend-configuration-pn/models';
import * as R from 'ramda';
import {TaskWizardStatusesEnum} from 'src/app/plugins/modules/backend-configuration-pn/enums';
import {FormsModule} from '@angular/forms';
import {TranslatePipe} from '@ngx-translate/core';
import {NgSelectComponent} from '@ng-select/ng-select';

@Component({
    selector: 'app-task-tracker-select-worker-modal',
    imports: [FormsModule, TranslatePipe, MatDialogTitle, MatDialogContent, MatDialogActions, NgSelectComponent],
    templateUrl: './task-tracker-select-worker-modal.component.html',
    styleUrls: ['./task-tracker-select-worker-modal.component.scss'],
})
export class TaskTrackerSelectWorkerModalComponent implements OnInit, OnDestroy {
  public dialogRef = inject(MatDialogRef<TaskTrackerSelectWorkerModalComponent>);
  private injectedWorkers = inject<WorkerModel[]>(MAT_DIALOG_DATA);

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
