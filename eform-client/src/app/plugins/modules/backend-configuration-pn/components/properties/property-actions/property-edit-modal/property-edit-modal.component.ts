import {
  Component,
  EventEmitter,
  Inject,
  OnDestroy,
  OnInit,
} from '@angular/core';
import {applicationLanguages} from 'src/app/common/const';
import {PropertyModel, PropertyUpdateModel} from '../../../../models';
import {AuthStateService} from 'src/app/common/store';
import {BackendConfigurationPnPropertiesService} from '../../../../services';
import {MAT_DIALOG_DATA, MatDialogRef} from '@angular/material/dialog';
import {Subscription} from 'rxjs';
import {AutoUnsubscribe} from 'ngx-auto-unsubscribe';
import {selectAuthIsAuth} from 'src/app/state/auth/auth.selector';
import {Store} from '@ngrx/store';

@AutoUnsubscribe()
@Component({
    selector: 'app-property-edit-modal',
    templateUrl: './property-edit-modal.component.html',
    styleUrls: ['./property-edit-modal.component.scss'],
    standalone: false
})
export class PropertyEditModalComponent implements OnInit, OnDestroy {
  propertyUpdate: EventEmitter<PropertyUpdateModel> = new EventEmitter<PropertyUpdateModel>();
  selectedProperty: PropertyUpdateModel = new PropertyUpdateModel();
  selectedLanguages: { id: number; checked: boolean }[] = [];
  propertyIsFarm: boolean = false;

  getChrInformationSub$: Subscription;
  getCompanyTypeSub$: Subscription;
  public isAuth$ = this.store.select(selectAuthIsAuth);
  public selectAuthIsAdmin$ = this.store.select(selectAuthIsAuth);

  get applicationLanguages() {
    return applicationLanguages;
  }

  constructor(
    private store: Store,
    public authStateService: AuthStateService,
    private propertiesService: BackendConfigurationPnPropertiesService,
    public dialogRef: MatDialogRef<PropertyEditModalComponent>,
    @Inject(MAT_DIALOG_DATA) model: PropertyModel = new PropertyModel()
  ) {
    this.selectedLanguages = model.languages.map((x) => {
      return {id: x.id, checked: true};
    });
    this.selectedProperty = {
      ...model,
      languagesIds: [],
    };
  }

  ngOnInit() {
  }

  hide() {
    this.dialogRef.close();
    this.selectedProperty = new PropertyUpdateModel();
    this.selectedLanguages = [];
  }

  onUpdateProperty() {
    this.propertyUpdate.emit({
      ...this.selectedProperty,
      languagesIds: this.selectedLanguages.map((x) => x.id),
    });
  }

  onNameFilterChanged(number: number) {
    // if (number === 0) {
    //   this.propertyIsFarm = false;
    // }
    // if (number === 1111111) {
      this.propertyIsFarm = true;
      this.selectedProperty.isFarm = true;
    // }
    // if (number > 1111111) {
    //   if (number.toString().length > 7) {
    //     this.getCompanyTypeSub$ = this.propertiesService.getCompanyType(number)
    //       .subscribe((data) => {
    //         if (data && data.success) {
    //           if (data.model.industrycode.toString().slice(0, 2) === '01') {
    //             this.propertyIsFarm = true;
    //             this.selectedProperty.isFarm = true;
    //             if (data.model.error !== 'NOT_FOUND') {
    //               this.selectedProperty.address = data.model.address + ', ' + data.model.city;
    //               this.selectedProperty.name = data.model.name;
    //               this.selectedProperty.industryCode = data.model.industrycode;
    //             }
    //           } else {
    //             if (data.model.error === 'REQUIRES_PAID_SUBSCRIPTION') {
    //               this.propertyIsFarm = true;
    //               this.selectedProperty.isFarm = true;
    //             } else {
    //               this.propertyIsFarm = false;
    //               this.selectedProperty.isFarm = false;
    //               if (data.model.error !== 'NOT_FOUND') {
    //                 this.selectedProperty.address = data.model.address + ', ' + data.model.city;
    //                 this.selectedProperty.name = data.model.name;
    //                 this.selectedProperty.industryCode = data.model.industrycode;
    //               }
    //             }
    //           }
    //         }
    //       });
    //   }
    // } else {
    //   // this.selectedProperty.name = '';
    //   // this.selectedProperty.address = '';
    // }
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
    if (this.selectedProperty/* && this.selectedProperty.languagesIds*/) {
      return (
        !this.selectedProperty.name/* ||
        !this.selectedLanguages.some((x) => x.checked)*/
      );
    }
    return false;
  }

  onChrNumberChanged(number: number) {
    if (number > 11111) {
      if (number.toString().length > 5) {
        this.getChrInformationSub$ = this.propertiesService.getChrInformation(number)
          .subscribe((data) => {
            if (data && data.success) {
              if (data.model.ejendom.byNavn === '' || data.model.ejendom.byNavn === null) {
                this.selectedProperty.address = data.model.ejendom.adresse + ', ' + data.model.ejendom.postDistrikt;
              } else {
                this.selectedProperty.address = data.model.ejendom.adresse + ', ' + data.model.ejendom.byNavn;
              }
            }
          });
      }
    }
  }

  ngOnDestroy(): void {
  }
}
