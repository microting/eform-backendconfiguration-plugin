import { Component, EventEmitter, OnDestroy, OnInit } from '@angular/core';
import { FormBuilder, FormGroup, Validators } from '@angular/forms';
import { applicationLanguages, applicationLanguagesTranslated } from 'src/app/common/const';
import { PropertyCreateModel } from '../../../../models';
import { AuthStateService } from 'src/app/common/store';
import { BackendConfigurationPnPropertiesService } from '../../../../services';
import { MatDialogRef } from '@angular/material/dialog';
import { AutoUnsubscribe } from 'ngx-auto-unsubscribe';
import { Subscription } from 'rxjs';

@AutoUnsubscribe()
@Component({
  selector: 'app-property-create-modal',
  templateUrl: './property-create-modal.component.html',
  styleUrls: ['./property-create-modal.component.scss'],
  standalone: false
})
export class PropertyCreateModalComponent implements OnInit, OnDestroy {
  propertyCreate: EventEmitter<PropertyCreateModel> = new EventEmitter<PropertyCreateModel>();
  newPropertyForm: FormGroup;
  newProperty: PropertyCreateModel = new PropertyCreateModel();
  selectedLanguages: { id: number; checked: boolean }[] = [];
  propertyIsFarm: boolean = false;

  getCompanyTypeSub$: Subscription;
  getChrInformationSub$: Subscription;

  get applicationLanguages() {
    return applicationLanguages;
  }

  constructor(
    private fb: FormBuilder,
    public authStateService: AuthStateService,
    private propertiesService: BackendConfigurationPnPropertiesService,
    public dialogRef: MatDialogRef<PropertyCreateModalComponent>
  ) {
    this.propertyIsFarm = false;

    // Initialize reactive form
    this.newPropertyForm = this.fb.group({
      cvr: ['', Validators.required],
      mainMailAddress: ['', [Validators.required, Validators.email]],
      name: ['', Validators.required],
      chr: [''],
      address: [''],
      workorderEnable: [false],
      isFarm: [false]
    });
  }

  ngOnDestroy(): void {
  }

  ngOnInit() {
  }

  hide() {
    this.dialogRef.close();
    this.newPropertyForm.reset();
    this.selectedLanguages = [];
    this.propertyIsFarm = false;
  }

  onCreateProperty() {
    const newProperty: PropertyCreateModel = {
      ...this.newPropertyForm.value,
      languagesIds: applicationLanguagesTranslated.map(x => x.id)
    };
    this.propertyCreate.emit(newProperty);
  }
  onNameFilterChanged(number: string) {
    this.newPropertyForm.patchValue({ cvr: number });

    // if (+number === 0) {
    //   this.propertyIsFarm = false;
    //   this.newPropertyForm.patchValue({ isFarm: false });
    // }
    //
    // if (+number === 1111111) {
    this.propertyIsFarm = true;
    this.newPropertyForm.patchValue({ isFarm: true });
    // }
    //
    // if (+number > 1111111 && number.toString().length > 7) {
    //   this.getCompanyTypeSub$ = this.propertiesService.getCompanyType(+number)
    //     .subscribe((data) => {
    //       if (data?.success) {
    //         if (data.model.industrycode.toString().slice(0, 2) === '01') {
    //           this.propertyIsFarm = true;
    //           this.newPropertyForm.patchValue({
    //             isFarm: true,
    //             name: data.model.name,
    //             address: `${data.model.address}, ${data.model.city}`,
    //             industryCode: data.model.industrycode
    //           });
    //         } else {
    //           this.propertyIsFarm = data.model.error === 'REQUIRES_PAID_SUBSCRIPTION';
    //           this.newPropertyForm.patchValue({
    //             isFarm: this.propertyIsFarm,
    //             name: data.model.name || '',
    //             address: data.model.address ? `${data.model.address}, ${data.model.city}` : '',
    //             industryCode: data.model.industrycode || ''
    //           });
    //         }
    //       }
    //     });
    // } else {
    //   this.newPropertyForm.patchValue({ name: '', address: '' });
    // }
  }

  onChrNumberChanged(number: number) {
    if (number > 11111 && number.toString().length > 5) {
      this.getChrInformationSub$ = this.propertiesService.getChrInformation(number)
        .subscribe((data) => {
          if (data?.success) {
            const address = data.model.ejendom.byNavn || '';
            this.newPropertyForm.patchValue({
              address: `${data.model.ejendom.adresse}, ${address || data.model.ejendom.postDistrikt}`
            });
          }
        });
    }
  }

  get isDisabledSaveButton(): boolean {
    return this.newPropertyForm.invalid;
  }
}
