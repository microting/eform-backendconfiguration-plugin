import {
  Component,
  EventEmitter,
  OnInit,
  Output,
  ViewChild,
} from '@angular/core';
import {applicationLanguages, applicationLanguagesTranslated} from 'src/app/common/const';
import { PropertyCreateModel } from '../../../../models';
import {AuthStateService} from 'src/app/common/store';

@Component({
  selector: 'app-property-create-modal',
  templateUrl: './property-create-modal.component.html',
  styleUrls: ['./property-create-modal.component.scss'],
})
export class PropertyCreateModalComponent implements OnInit {
  @ViewChild('frame', { static: false }) frame;
  @Output()
  propertyCreate: EventEmitter<PropertyCreateModel> = new EventEmitter<PropertyCreateModel>();
  newProperty: PropertyCreateModel = new PropertyCreateModel();
  selectedLanguages: { id: number; checked: boolean }[] = [];

  get applicationLanguages() {
    return applicationLanguages;
  }

  constructor(
    public authStateService: AuthStateService) {
  }

  ngOnInit() {}

  show() {
    this.frame.show();
  }

  hide() {
    this.frame.hide();
    this.newProperty = new PropertyCreateModel();
    this.selectedLanguages = [];
  }

  onCreateProperty() {
    this.propertyCreate.emit({
      ...this.newProperty,
      languagesIds: applicationLanguagesTranslated.map((x) => x.id),
    });
  }

  // addToArray(e: any, languageId: number) {
  //   if (e.target.checked) {
  //     this.selectedLanguages = [
  //       ...this.selectedLanguages,
  //       { id: languageId, checked: true },
  //     ];
  //   } else {
  //     this.selectedLanguages = this.selectedLanguages.filter(
  //       (x) => x.id !== languageId
  //     );
  //   }
  // }

  getLanguageIsChecked(languageId: number): boolean {
    const language = this.selectedLanguages.find((x) => x.id === languageId);
    return language ? language.checked : false;
  }

  get isDisabledSaveButton(): boolean {
    if (this.newProperty /*&& this.newProperty.languagesIds*/) {
      return (
        !this.newProperty.name/* || !this.selectedLanguages.some((x) => x.checked)*/
      );
    }
    return false;
  }
}
