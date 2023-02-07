import {
  Component,
  EventEmitter,
  Inject,
  OnDestroy,
  OnInit, Output,
} from '@angular/core';
import {AutoUnsubscribe} from 'ngx-auto-unsubscribe';
import {Subscription} from 'rxjs';
import {CommonDictionaryModel} from 'src/app/common/models';
import {applicationLanguages2} from 'src/app/common/const';
import {PropertyAssignmentWorkerModel, DeviceUserModel} from '../../../../models';
import {BackendConfigurationPnPropertiesService} from '../../../../services';
import {AuthStateService} from 'src/app/common/store';
import {MAT_DIALOG_DATA, MatDialogRef} from '@angular/material/dialog';
import {MtxGridColumn} from '@ng-matero/extensions/grid';
import {TranslateService} from '@ngx-translate/core';

@AutoUnsubscribe()
@Component({
  selector: 'app-property-worker-create-edit-modal',
  templateUrl: './property-worker-create-edit-modal.component.html',
  styleUrls: ['./property-worker-create-edit-modal.component.scss'],
})
export class PropertyWorkerCreateEditModalComponent implements OnInit, OnDestroy {
  availableProperties: CommonDictionaryModel[] = [];
  edit: boolean = false;
  selectedDeviceUser: DeviceUserModel = new DeviceUserModel();
  selectedDeviceUserCopy: DeviceUserModel = new DeviceUserModel();
  assignments: PropertyAssignmentWorkerModel[] = [];
  @Output() userUpdated: EventEmitter<void> = new EventEmitter<void>();
  tableHeaders: MtxGridColumn[] = [
    {
      header: this.translateService.stream('ID'),
      field: 'id',
    },
    {
      header: this.translateService.stream('Property name'),
      field: 'name',
    },
    {
      header: this.translateService.stream('Select'),
      field: 'select',
    },
  ];

  deviceUserCreate$: Subscription;
  deviceUserUpdate$: Subscription;
  deviceUserAssign$: Subscription;

  constructor(
    public propertiesService: BackendConfigurationPnPropertiesService,
    public authStateService: AuthStateService,
    private translateService: TranslateService,
    public dialogRef: MatDialogRef<PropertyWorkerCreateEditModalComponent>,
    @Inject(MAT_DIALOG_DATA) model:
      {
        deviceUser: DeviceUserModel,
        assignments: PropertyAssignmentWorkerModel[],
        availableProperties: CommonDictionaryModel[],
      },
  ) {
    this.assignments = [...model.assignments];
    this.availableProperties = [...model.availableProperties];
    this.selectedDeviceUser = {...model.deviceUser ?? new DeviceUserModel()};
    this.selectedDeviceUserCopy = {...model.deviceUser};
  }

  get languages() {
    return applicationLanguages2;
  }

  ngOnInit() {
    if (this.selectedDeviceUser.id) {
      this.edit = true;
    }
    if (!this.edit) {
      this.selectedDeviceUser.languageCode = this.languages[0].locale;
      if (this.authStateService.checkClaim('task_management_enable')) {
        this.selectedDeviceUser.taskManagementEnabled = true;
      }
    }
  }

  hide(result = false) {
    this.selectedDeviceUser = new DeviceUserModel();
    this.assignments = [];
    this.dialogRef.close(result);
  }

  addToArray(e: any, propertyId: number) {
    const assignmentObject = new PropertyAssignmentWorkerModel();
    if (e.checked) {
      assignmentObject.isChecked = true;
      assignmentObject.propertyId = propertyId;
      this.assignments = [...this.assignments, assignmentObject];
    } else {
      this.assignments = this.assignments.filter(
        (x) => x.propertyId !== propertyId
      );
    }
  }

  getAssignmentIsCheckedByPropertyId(propertyId: number): boolean {
    const assignment = this.assignments.find(
      (x) => x.propertyId === propertyId
    );
    return assignment ? assignment.isChecked : false;
  }

  getAssignmentIsLockedByPropertyId(propertyId: number): boolean {
    const assignment = this.assignments.find(
      (x) => x.propertyId === propertyId
    );
    return assignment ? assignment.isLocked : false;
  }

  updateSingle() {
    if (
      this.selectedDeviceUserCopy.userFirstName !==
      this.selectedDeviceUser.userFirstName ||
      this.selectedDeviceUserCopy.userLastName !==
      this.selectedDeviceUser.userLastName ||
      this.selectedDeviceUserCopy.language !==
      this.selectedDeviceUser.language ||
      this.selectedDeviceUserCopy.languageCode !==
      this.selectedDeviceUser.languageCode ||
      this.selectedDeviceUserCopy.timeRegistrationEnabled !==
      this.selectedDeviceUser.timeRegistrationEnabled
    ) {
      // if fields device user edited
      this.deviceUserCreate$ = this.propertiesService
        .updateSingleDeviceUser(this.selectedDeviceUser)
        .subscribe((operation) => {
          if (operation && operation.success && this.assignments) {
            this.assignWorkerToPropertiesUpdate();
          }
        });
    } else {
      this.assignWorkerToPropertiesUpdate();
    }
  }

  createDeviceUser() {
    this.deviceUserCreate$ = this.propertiesService
      .createSingleDeviceUser(this.selectedDeviceUser)
      .subscribe((operation) => {
        if (operation && operation.success) {
          if (this.assignments && this.assignments.length > 0) {
            this.assignWorkerToProperties(operation.model);
          }
          this.hide(true);
        }
      });
  }

  assignWorkerToProperties(siteId: number) {
    this.deviceUserAssign$ = this.propertiesService
      .assignPropertiesToWorker({
        siteId,
        assignments: this.assignments,
        timeRegistrationEnabled: this.selectedDeviceUser.taskManagementEnabled,
        taskManagementEnabled: this.selectedDeviceUser.taskManagementEnabled})
      .subscribe((operation) => {
        if (operation && operation.success) {
          this.hide(true);
        }
      });
  }

  assignWorkerToPropertiesUpdate() {
    this.deviceUserAssign$ = this.propertiesService
      .updateAssignPropertiesToWorker({
        siteId: this.selectedDeviceUser.normalId,
        assignments: this.assignments,
        timeRegistrationEnabled: this.selectedDeviceUser.timeRegistrationEnabled,
        taskManagementEnabled: this.selectedDeviceUser.taskManagementEnabled,
      })
      .subscribe((operation) => {
        if (operation && operation.success) {
          //this.userUpdated.emit();
          this.hide(true);
        }
      });
  }

  getAssignmentByPropertyId(propertyId: number): PropertyAssignmentWorkerModel {
    return (
      this.assignments.find((x) => x.propertyId === propertyId) ?? {
        propertyId: propertyId,
        isChecked: false,
        isLocked: false,
      }
    );
  }

  ngOnDestroy(): void {
  }
}
