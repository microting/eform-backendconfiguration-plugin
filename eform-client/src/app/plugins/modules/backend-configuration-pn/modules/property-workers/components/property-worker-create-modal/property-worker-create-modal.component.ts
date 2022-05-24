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
import {applicationLanguages, applicationLanguagesTranslated} from 'src/app/common/const/application-languages.const';
import { BackendConfigurationPnPropertiesService } from '../../../../services';
import { PropertyAssignmentWorkerModel } from '../../../../models/properties/property-workers-assignment.model';
import {DeviceUserModel} from 'src/app/plugins/modules/backend-configuration-pn/models/device-users';

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
    public propertiesService: BackendConfigurationPnPropertiesService
  ) {}

  ngOnInit() {
    this.languages = applicationLanguagesTranslated;
    // this.languages = [
    //   { id: 1, locale: 'da', text: 'Danish' },
    //   { id: 2, locale: 'en-US', text: 'English' },
  // ];
  }

  show() {
    this.simpleSiteModel.languageCode = this.languages[0].locale;
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
      assignmentObject.propertyId = propertyId;
      this.assignments = [...this.assignments, assignmentObject];
    } else {
      this.assignments = this.assignments.filter(
        (x) => x.propertyId !== propertyId
      );
    }
  }

  createDeviceUser() {
    this.deviceUserCreate$ = this.propertiesService
      .createSingleDeviceUser(this.simpleSiteModel)
      .subscribe((operation) => {
        if (operation && operation.success) {
          if (this.assignments && this.assignments.length > 0) {
            this.assignWorkerToProperties(operation.model);
          } else {
            this.deviceUserCreated.emit();
            this.hide();
          }
        }
      });
  }

  assignWorkerToProperties(siteId: number) {
    this.deviceUserAssign$ = this.propertiesService
      .assignPropertiesToWorker({ siteId, assignments: this.assignments, timeRegistrationEnabled: false })
      .subscribe((operation) => {
        if (operation && operation.success) {
          this.deviceUserCreated.emit();
          this.hide();
        }
      });
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
