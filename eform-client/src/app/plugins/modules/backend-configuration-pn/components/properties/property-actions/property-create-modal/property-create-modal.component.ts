import {
  Component,
  EventEmitter,
  OnDestroy,
  OnInit,
} from '@angular/core';
import {applicationLanguages, applicationLanguagesTranslated} from 'src/app/common/const';
import {PropertyCreateModel} from '../../../../models';
import {AuthStateService} from 'src/app/common/store';
import {BackendConfigurationPnPropertiesService} from '../../../../services';
import {MatDialogRef} from '@angular/material/dialog';
import {AutoUnsubscribe} from 'ngx-auto-unsubscribe';
import {Subscription} from 'rxjs';

@AutoUnsubscribe()
@Component({
  selector: 'app-property-create-modal',
  templateUrl: './property-create-modal.component.html',
  styleUrls: ['./property-create-modal.component.scss'],
})
export class PropertyCreateModalComponent implements OnInit, OnDestroy {
  propertyCreate: EventEmitter<PropertyCreateModel> = new EventEmitter<PropertyCreateModel>();
  newProperty: PropertyCreateModel = new PropertyCreateModel();
  selectedLanguages: { id: number; checked: boolean }[] = [];
  propertyIsFarm: boolean = false;

  getCompanyTypeSub$: Subscription;
  getChrInformationSub$: Subscription;

  get applicationLanguages() {
    return applicationLanguages;
  }

  constructor(
    public authStateService: AuthStateService,
    private propertiesService: BackendConfigurationPnPropertiesService,
    public dialogRef: MatDialogRef<PropertyCreateModalComponent>,
  ) {
    this.propertyIsFarm = false;
  }

  ngOnDestroy(): void {
  }

  ngOnInit() {
  }

  hide() {
    this.dialogRef.close();
    this.newProperty = new PropertyCreateModel();
    this.selectedLanguages = [];
  }

  onCreateProperty() {
    this.propertyCreate.emit({
      ...this.newProperty,
      languagesIds: applicationLanguagesTranslated.map((x) => x.id),
    });
  }


  onNameFilterChanged(number: number) {
    this.newProperty.cvr = number.toString();
    if (number === 0) {
      this.propertyIsFarm = false;
    }
    if (number === 1111111) {
      this.propertyIsFarm = true;
      this.newProperty.isFarm = true;
    }
    if (number > 1111111) {
      if (number.toString().length > 7) {
        this.getCompanyTypeSub$ = this.propertiesService.getCompanyType(number)
          .subscribe((data) => {
            if (data && data.success) {
              if (data.model.industrycode.toString().slice(0, 2) === '01') {
                this.propertyIsFarm = true;
                this.newProperty.isFarm = true;
                if (data.model.error !== 'NOT_FOUND') {
                  this.newProperty.address = data.model.address + ', ' + data.model.city;
                  this.newProperty.name = data.model.name;
                  this.newProperty.industryCode = data.model.industrycode;
                }
              } else {
                if (data.model.error === 'REQUIRES_PAID_SUBSCRIPTION') {
                  this.propertyIsFarm = true;
                  this.newProperty.isFarm = true;
                } else {
                  this.propertyIsFarm = false;
                  this.newProperty.isFarm = false;
                  if (data.model.error !== 'NOT_FOUND') {
                    this.newProperty.address = data.model.address + ', ' + data.model.city;
                    this.newProperty.name = data.model.name;
                    this.newProperty.industryCode = data.model.industrycode;
                  }
                }
              }
            }
          });
      }
    } else {
      this.newProperty.name = '';
      this.newProperty.address = '';
    }
  }

  onChrNumberChanged(number: number) {
    if (number > 11111) {
      if (number.toString().length > 5) {
        this.getChrInformationSub$ = this.propertiesService.getChrInformation(number)
          .subscribe((data) => {
            if (data && data.success) {
              //debugger;
              if (data.model.ejendom.byNavn === '' || data.model.ejendom.byNavn === null) {
                this.newProperty.address = data.model.ejendom.adresse + ', ' + data.model.ejendom.postDistrikt;
              } else {
                this.newProperty.address = data.model.ejendom.adresse + ', ' + data.model.ejendom.byNavn;
              }
            }
          });
      }
    }
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
