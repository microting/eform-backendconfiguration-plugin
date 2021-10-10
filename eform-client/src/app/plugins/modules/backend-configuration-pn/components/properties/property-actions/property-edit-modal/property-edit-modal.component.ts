import {
  Component,
  EventEmitter,
  OnInit,
  Output,
  ViewChild,
} from '@angular/core';
import { applicationLanguages } from 'src/app/common/const';
import { PropertyModel, PropertyUpdateModel } from '../../../../models';

@Component({
  selector: 'app-property-edit-modal',
  templateUrl: './property-edit-modal.component.html',
  styleUrls: ['./property-edit-modal.component.scss'],
})
export class PropertyEditModalComponent implements OnInit {
  @ViewChild('frame', { static: false }) frame;
  @Output()
  propertyUpdate: EventEmitter<PropertyUpdateModel> = new EventEmitter<PropertyUpdateModel>();
  selectedProperty: PropertyUpdateModel = new PropertyUpdateModel();
  selectedLanguages: { id: number; checked: boolean }[] = [];

  get applicationLanguages() {
    return applicationLanguages;
  }

  constructor() {}

  ngOnInit() {}

  show(model: PropertyModel) {
    this.selectedLanguages = model.languages.map((x) => {
      return { id: x.id, checked: true };
    });
    this.selectedProperty = {
      ...model,
      languagesIds: [],
    };
    this.frame.show();
  }

  hide() {
    this.frame.hide();
    this.selectedProperty = new PropertyUpdateModel();
    this.selectedLanguages = [];
  }

  onUpdateProperty() {
    this.propertyUpdate.emit({
      ...this.selectedProperty,
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

  get isDisabledSaveButton(): boolean {
    if (this.selectedProperty && this.selectedProperty.languagesIds) {
      return (
        !this.selectedProperty.name ||
        !this.selectedLanguages.some((x) => x.checked)
      );
    }
  }
}
