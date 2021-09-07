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
import { DeviceUserModel } from 'src/app/common/models/device-users';
import { DeviceUserService } from 'src/app/common/services/device-users';
import { applicationLanguages } from 'src/app/common/const/application-languages.const';
import { PropertyAssignmentWorkerModel } from '../../../../models/properties/property-workers-assignment.model';
import { BackendConfigurationPnPropertiesService } from '../../../../services';

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

  assignments: PropertyAssignmentWorkerModel[] = [];

  deviceUserCreate$: Subscription;
  deviceUserAssign$: Subscription;
  deviceUserAssignments$: Subscription;

  constructor(
    private deviceUserService: DeviceUserService,
    public propertiesService: BackendConfigurationPnPropertiesService
  ) {}

  ngOnInit() {}

  show(
    deviceUser: DeviceUserModel,
    assignments: PropertyAssignmentWorkerModel[]
  ) {
    this.selectedDeviceUser = { ...deviceUser };
    this.assignments = [...assignments];
    this.frame.show();
  }

  hide() {
    this.selectedDeviceUser = new DeviceUserModel();
    this.assignments = [];
  }

  addToArray(e: any, propertyId: number) {
    const assignmentObject = new PropertyAssignmentWorkerModel();
    if (e.target.checked) {
      assignmentObject.isChecked = true;
      this.assignments = [...this.assignments, assignmentObject];
    } else {
      this.assignments = this.assignments.filter(
        (x) => x.propertyId !== propertyId
      );
    }
  }

  updateSingle() {
    this.deviceUserCreate$ = this.deviceUserService
      .updateSingleDeviceUser(this.selectedDeviceUser)
      .subscribe((operation) => {
        if (operation && operation.success) {
          if (this.assignments && this.assignments.length > 0) {
            this.assignWorkerToProperties();
          } else {
            this.userUpdated.emit();
            this.hide();
          }
        }
      });
  }

  assignWorkerToProperties() {
    this.deviceUserAssign$ = this.propertiesService
      .assignPropertiesToWorker({
        siteId: this.selectedDeviceUser.id,
        assignments: this.assignments,
      })
      .subscribe((operation) => {
        if (operation && operation.success) {
          this.userUpdated.emit();
          this.hide();
        }
      });
  }

  get languages() {
    return applicationLanguages;
  }

  ngOnDestroy(): void {}
}
