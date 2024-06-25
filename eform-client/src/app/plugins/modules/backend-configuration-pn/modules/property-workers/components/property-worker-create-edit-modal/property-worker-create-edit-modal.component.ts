import {
  Component,
  EventEmitter,
  Inject,
  OnDestroy,
  OnInit, Output,
} from '@angular/core';
import {AutoUnsubscribe} from 'ngx-auto-unsubscribe';
import {Subscription} from 'rxjs';
import {CommonDictionaryModel, LanguagesModel} from 'src/app/common/models';
import {PropertyAssignmentWorkerModel, DeviceUserModel} from '../../../../models';
import {BackendConfigurationPnPropertiesService} from '../../../../services';
import {AuthStateService} from 'src/app/common/store';
import {MAT_DIALOG_DATA, MatDialogRef} from '@angular/material/dialog';
import {MtxGridColumn} from '@ng-matero/extensions/grid';
import {TranslateService} from '@ngx-translate/core';
import {tap} from 'rxjs/operators';
import {AppSettingsStateService} from 'src/app/modules/application-settings/components/store';

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
      class: 'propertyName',
    },
    {
      header: this.translateService.stream('Select'),
      field: 'select',
    },
  ];

  deviceUserCreate$: Subscription;
  deviceUserUpdate$: Subscription;
  deviceUserAssign$: Subscription;
  getLanguagesSub$: Subscription;
  appLanguages: LanguagesModel = new LanguagesModel();
  activeLanguages: Array<any> = [];

  constructor(
    public propertiesService: BackendConfigurationPnPropertiesService,
    public authStateService: AuthStateService,
    private translateService: TranslateService,
    public dialogRef: MatDialogRef<PropertyWorkerCreateEditModalComponent>,
    private appSettingsStateService: AppSettingsStateService,
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
    return this.appLanguages.languages.filter((x) => x.isActive);
  }

  ngOnInit() {
    this.getEnabledLanguages();
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
      this.selectedDeviceUser.timeRegistrationEnabled ||
      this.selectedDeviceUser.employeeNo !==
      this.selectedDeviceUserCopy.employeeNo ||
      this.selectedDeviceUser.pinCode !== '****'
    ) {
      // if fields device user edited
      this.selectedDeviceUser.siteUid = this.selectedDeviceUser.id;
      this.deviceUserCreate$ = this.propertiesService
        .updateSingleDeviceUser(this.selectedDeviceUser)
        .subscribe((operation) => {
          if (operation && operation.success && this.assignments) {
            this.assignWorkerToPropertiesUpdate();
          } else {
            this.hide(true);
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
          } else {
            this.hide(true);
          }
        }
      });
  }

  assignWorkerToProperties(siteId: number) {
    this.deviceUserAssign$ = this.propertiesService
      .assignPropertiesToWorker({
        siteId,
        assignments: this.assignments,
        timeRegistrationEnabled: this.selectedDeviceUser.timeRegistrationEnabled,
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

  getEnabledLanguages() {
    this.getLanguagesSub$ = this.appSettingsStateService.getLanguages()
      .pipe(tap(data => {
        if (data && data.success && data.model) {
          this.appLanguages = data.model;
          this.activeLanguages = this.appLanguages.languages.filter((x) => x.isActive);
          if (this.selectedDeviceUser.id) {
            this.edit = true;
          }
          if (!this.edit) {
            this.selectedDeviceUser.languageCode = this.languages[0].languageCode;
            if (this.authStateService.checkClaim('task_management_enable')) {
              this.selectedDeviceUser.taskManagementEnabled = false;
            }
          }
        }
      }))
      .subscribe();
  }
}
