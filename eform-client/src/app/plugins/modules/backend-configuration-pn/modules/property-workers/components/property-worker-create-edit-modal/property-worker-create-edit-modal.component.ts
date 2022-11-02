import {
  Component,
  Inject,
  OnDestroy,
  OnInit,
} from '@angular/core';
import {AutoUnsubscribe} from 'ngx-auto-unsubscribe';
import {Subscription} from 'rxjs';
import {CommonDictionaryModel} from 'src/app/common/models';
import {applicationLanguagesTranslated} from 'src/app/common/const';
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
  tableHeaders: MtxGridColumn[] = [
    {
      header: this.translateService.stream('ID'),
      field: 'id',
    },
    {
      header: this.translateService.stream('Property name'),
      field: 'name',
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
    return applicationLanguagesTranslated;
  }

  get selectedProperties() {
    const checkedAssignments = this.assignments.filter(x => x.isChecked);
    return this.availableProperties.filter(x => checkedAssignments.some(y => y.propertyId === x.id));
  }

  ngOnInit() {
    if (this.selectedDeviceUser.id) {
      this.edit = true;
    }
    if (!this.edit) {
      this.selectedDeviceUser.languageCode = this.languages[1].locale;
    }
  }

  hide(result = false) {
    this.selectedDeviceUser = new DeviceUserModel();
    this.assignments = [];
    this.dialogRef.close(result);
  }

  changeArray(properties: CommonDictionaryModel[]) {
    this.assignments = [];
    properties.forEach(property => this.assignments = [...this.assignments, {propertyId: property.id, isChecked: true}]);
  }

  updateSingle() {
    if (
      this.selectedDeviceUserCopy.userFirstName !== this.selectedDeviceUser.userFirstName ||
      this.selectedDeviceUserCopy.userLastName !== this.selectedDeviceUser.userLastName ||
      this.selectedDeviceUserCopy.language !== this.selectedDeviceUser.language ||
      this.selectedDeviceUserCopy.languageCode !== this.selectedDeviceUser.languageCode ||
      this.selectedDeviceUserCopy.timeRegistrationEnabled !== this.selectedDeviceUser.timeRegistrationEnabled
    ) {
      // if fields device user edited
      this.deviceUserUpdate$ = this.propertiesService
        .updateSingleDeviceUser(this.selectedDeviceUser)
        .subscribe((operation) => {
          if (operation && operation.success) {
            if (this.assignments) {
              this.assignWorkerToProperties();
            } else {
              this.hide(true);
            }
          }
        });
    } else {
      this.assignWorkerToProperties();
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

  assignWorkerToProperties(siteId?: number) {
    this.deviceUserAssign$ = this.propertiesService
      .updateAssignPropertiesToWorker({
        siteId: siteId ?? this.selectedDeviceUser.normalId,
        assignments: this.assignments,
        timeRegistrationEnabled: false,
      })
      .subscribe((operation) => {
        if (operation && operation.success) {
          this.hide(true);
        }
      });
  }

  getAssignmentByPropertyId(propertyId: number): PropertyAssignmentWorkerModel {
    return (
      this.assignments.find((x) => x.propertyId === propertyId) ?? {
        propertyId: propertyId,
        isChecked: false,
      }
    );
  }

  ngOnDestroy(): void {
  }
}
