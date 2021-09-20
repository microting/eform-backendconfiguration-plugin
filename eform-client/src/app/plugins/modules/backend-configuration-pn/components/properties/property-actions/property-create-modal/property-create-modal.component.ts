import {
  Component,
  EventEmitter,
  OnInit,
  Output,
  ViewChild,
} from '@angular/core';
import { applicationLanguages } from 'src/app/common/const';
import { PropertyAssignmentWorkerModel } from 'src/app/plugins/modules/backend-configuration-pn/models/properties/property-workers-assignment.model';
import { PropertyCreateModel } from '../../../../models';

@Component({
  selector: 'app-property-create-modal',
  templateUrl: './property-create-modal.component.html',
  styleUrls: ['./property-create-modal.component.scss'],
})
export class PropertyCreateModalComponent implements OnInit {
  @ViewChild('frame', { static: false }) frame;
  @Output() propertyCreate: EventEmitter<PropertyCreateModel> =
    new EventEmitter<PropertyCreateModel>();
  newProperty: PropertyCreateModel = new PropertyCreateModel();
  selectedLanguages: { id: number; checked: boolean }[] = [];

  get applicationLanguages() {
    return applicationLanguages;
  }

  constructor() {}

  ngOnInit() {}

  show() {
    this.frame.show();
  }

  hide() {
    this.newProperty = new PropertyCreateModel();
    this.frame.hide();
  }

  onCreateProperty() {
    this.propertyCreate.emit({
      ...this.newProperty,
      languagesIds: this.selectedLanguages.map((x) => x.id),
    });
  }

  addToArray(e: any, languageId: number) {
    if (e.target.checked) {
      this.selectedLanguages = [
        ...this.selectedLanguages,
        { id: languageId, checked: true },
      ];
    } else {
      this.selectedLanguages = this.selectedLanguages.filter(
        (x) => x.id !== languageId
      );
    }
  }

  getLanguageIsChecked(languageId: number): boolean {
    const language = this.selectedLanguages.find((x) => x.id === languageId);
    return language ? language.checked : false;
  }
}
