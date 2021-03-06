import {
  Component,
  EventEmitter,
  Input,
  OnDestroy,
  OnInit,
  Output,
  ViewChild,
} from '@angular/core';
import { AutoUnsubscribe } from 'ngx-auto-unsubscribe';
import { Subscription } from 'rxjs';
import { CommonDictionaryModel } from 'src/app/common/models';
import { applicationLanguagesTranslated } from 'src/app/common/const/application-languages.const';
import { PropertyAssignmentWorkerModel } from '../../../../models/properties/property-workers-assignment.model';
import { BackendConfigurationPnPropertiesService } from '../../../../services';
import {DeviceUserModel} from 'src/app/plugins/modules/backend-configuration-pn/models/device-users';
import {AuthStateService} from 'src/app/common/store';

@AutoUnsubscribe()
@Component({
  selector: 'app-edit-device-user-modal',
  templateUrl: './property-worker-edit-modal.component.html',
  styleUrls: ['./property-worker-edit-modal.component.scss'],
})
export class PropertyWorkerEditModalComponent implements OnInit, OnDestroy {
  @Input() availableProperties: CommonDictionaryModel[] = [];
  @Output() userUpdated: EventEmitter<void> = new EventEmitter<void>();
  @ViewChild('frame', { static: true }) frame;
  selectedDeviceUser: DeviceUserModel = new DeviceUserModel();
  selectedDeviceUserCopy: DeviceUserModel = new DeviceUserModel();

  assignments: PropertyAssignmentWorkerModel[] = [];

  deviceUserCreate$: Subscription;
  deviceUserAssign$: Subscription;
  deviceUserAssignments$: Subscription;

  constructor(
    // private deviceUserService: DeviceUserService,
    public propertiesService: BackendConfigurationPnPropertiesService,
    public authStateService: AuthStateService
  ) {}

  ngOnInit() {}

  show(
    deviceUser: DeviceUserModel,
    assignments: PropertyAssignmentWorkerModel[]
  ) {
    this.selectedDeviceUserCopy = { ...deviceUser };
    this.selectedDeviceUser = { ...deviceUser };
    this.assignments = [...assignments];
    this.frame.show();
  }

  hide() {
    this.selectedDeviceUser = new DeviceUserModel();
    this.assignments = [];
    this.frame.hide();
  }

  addToArray(e: any, propertyId: number) {
    const assignmentObject = new PropertyAssignmentWorkerModel();
    if (e.target.checked) {
      assignmentObject.isChecked = true;
      assignmentObject.propertyId = propertyId;
      this.assignments = [...this.assignments, assignmentObject];
    } else {
      this.assignments = this.assignments.filter(
        (x) => x.propertyId !== propertyId
      );
    }
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
          if (operation && operation.success) {
            if (this.assignments) {
              this.assignWorkerToProperties();
            } else {
              this.userUpdated.emit();
              this.hide();
            }
          }
        });
    } else {
      this.assignWorkerToProperties();
    }
  }

  assignWorkerToProperties() {
    this.deviceUserAssign$ = this.propertiesService
      .updateAssignPropertiesToWorker({
        siteId: this.selectedDeviceUser.normalId,
        assignments: this.assignments,
        timeRegistrationEnabled: false,
      })
      .subscribe((operation) => {
        if (operation && operation.success) {
          this.userUpdated.emit();
          this.hide();
        }
      });
  }

  get languages() {
    return applicationLanguagesTranslated;
  }

  getAssignmentIsCheckedByPropertyId(propertyId: number): boolean {
    const assignment = this.assignments.find(
      (x) => x.propertyId === propertyId
    );
    return assignment ? assignment.isChecked : false;
  }

  getAssignmentByPropertyId(propertyId: number): PropertyAssignmentWorkerModel {
    return (
      this.assignments.find((x) => x.propertyId === propertyId) ?? {
        propertyId: propertyId,
        isChecked: false,
      }
    );
  }

  ngOnDestroy(): void {}
}
