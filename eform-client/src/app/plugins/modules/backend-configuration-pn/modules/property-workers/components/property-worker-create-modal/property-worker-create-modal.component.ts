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
import { CommonDictionaryModel, DeviceUserModel } from 'src/app/common/models';
import { DeviceUserService } from 'src/app/common/services';
import { applicationLanguages } from 'src/app/common/const/application-languages.const';
import { BackendConfigurationPnPropertiesService } from '../../../../services';
import { PropertyAssignmentWorkerModel } from '../../../../models/properties/property-workers-assignment.model';

@AutoUnsubscribe()
@Component({
  selector: 'app-property-worker-create-modal',
  templateUrl: './property-worker-create-modal.component.html',
  styleUrls: ['./property-worker-create-modal.component.scss'],
})
export class PropertyWorkerCreateModalComponent implements OnInit, OnDestroy {
  @Input() availableProperties: CommonDictionaryModel[] = [];
  @Output() deviceUserCreated: EventEmitter<void> = new EventEmitter<void>();
  @ViewChild('frame', { static: true }) frame;
  simpleSiteModel: DeviceUserModel = new DeviceUserModel();
  assignments: PropertyAssignmentWorkerModel[] = [];
  languages: any;

  deviceUserCreate$: Subscription;
  deviceUserAssign$: Subscription;

  constructor(
    private deviceUserService: DeviceUserService,
    public propertiesService: BackendConfigurationPnPropertiesService
  ) {}

  ngOnInit() {
    this.languages = applicationLanguages;
  }

  show() {
    this.simpleSiteModel.languageCode = this.languages[1].locale;
    this.frame.show();
  }

  hide() {
    this.frame.hide();
    this.assignments = [];
    this.simpleSiteModel = new DeviceUserModel();
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

  createDeviceUser() {
    this.deviceUserCreate$ = this.deviceUserService
      .createSingleDeviceUser(this.simpleSiteModel)
      .subscribe((operation) => {
        if (operation && operation.success) {
          if (this.assignments && this.assignments.length > 0) {
            this.assignWorkerToProperties();
          } else {
            this.deviceUserCreated.emit();
            this.hide();
          }
        }
      });
  }

  assignWorkerToProperties() {
    this.deviceUserAssign$ = this.propertiesService
      .assignPropertiesToWorker({ siteId: 1, assignments: this.assignments })
      .subscribe((operation) => {
        if (operation && operation.success) {
          this.deviceUserCreated.emit();
          this.hide();
        }
      });
  }

  ngOnDestroy(): void {}
}
