import {
  Component,
  EventEmitter,
  OnDestroy,
  OnInit,
  inject
} from '@angular/core';
import { FormBuilder, FormGroup, Validators } from '@angular/forms';
import { applicationLanguages } from 'src/app/common/const';
import { PropertyModel, PropertyUpdateModel } from '../../../../models';
import { AuthStateService } from 'src/app/common/store';
import { BackendConfigurationPnPropertiesService } from '../../../../services';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { Subscription } from 'rxjs';
import { AutoUnsubscribe } from 'ngx-auto-unsubscribe';
import { Store } from '@ngrx/store';
import { selectAuthIsAuth } from 'src/app/state/auth/auth.selector';

@AutoUnsubscribe()
@Component({
  selector: 'app-property-edit-modal',
  templateUrl: './property-edit-modal.component.html',
  styleUrls: ['./property-edit-modal.component.scss'],
  standalone: false,
})
export class PropertyEditModalComponent implements OnInit, OnDestroy {
  private fb = inject(FormBuilder);
  private store = inject(Store);
  public authStateService = inject(AuthStateService);
  private propertiesService = inject(BackendConfigurationPnPropertiesService);
  public dialogRef = inject(MatDialogRef<PropertyEditModalComponent>);
  public model = inject<PropertyModel>(MAT_DIALOG_DATA);

  propertyUpdate: EventEmitter<PropertyUpdateModel> = new EventEmitter<PropertyUpdateModel>();
  editPropertyForm: FormGroup;
  propertyIsFarm = false;
  selectedLanguages: { id: number; checked: boolean }[] = [];

  getChrInformationSub$: Subscription;
  getCompanyTypeSub$: Subscription;
  public selectAuthIsAdmin$ = this.store.select(selectAuthIsAuth);

  

  ngOnInit() {
    this.selectedLanguages = this.model.languages.map((x) => ({ id: x.id, checked: true }));

    this.editPropertyForm = this.fb.group({
      cvr: [this.model.cvr, Validators.required],
      mainMailAddress: [this.model.mainMailAddress],
      name: [this.model.name, Validators.required],
      chr: [this.model.chr],
      address: [this.model.address],
      workorderEnable: [this.model.workorderEnable || false],
      isFarm: [this.model.isFarm || false],
      industryCode: [this.model.industryCode || '']
    });

    this.propertyIsFarm = this.model.isFarm || false;
}

  hide() {
    this.dialogRef.close();
    this.editPropertyForm.reset();
    this.selectedLanguages = [];
    this.propertyIsFarm = false;
  }

  onUpdateProperty() {
    const updatedProperty: PropertyUpdateModel = {
      ...this.model,
      ...this.editPropertyForm.value,
      languagesIds: this.selectedLanguages.map((x) => x.id),
    };
    this.propertyUpdate.emit(updatedProperty);
  }

  onNameFilterChanged(number: string) {
    this.editPropertyForm.patchValue({ cvr: number });

    // if (+number === 0) {
    //   this.propertyIsFarm = false;
    //   this.editPropertyForm.patchValue({ isFarm: false });
    // }
    //
    // if (+number === 1111111) {
    this.propertyIsFarm = true;
    this.editPropertyForm.patchValue({ isFarm: true });
    // }
    //
    // if (+number > 1111111 && number.toString().length > 7) {
    //   this.getCompanyTypeSub$ = this.propertiesService.getCompanyType(+number).subscribe((data) => {
    //     if (data?.success) {
    //       const industryPrefix = data.model.industrycode.toString().slice(0, 2);
    //       const isFarm = industryPrefix === '01' || data.model.error === 'REQUIRES_PAID_SUBSCRIPTION';
    //
    //       this.propertyIsFarm = isFarm;
    //       this.editPropertyForm.patchValue({
    //         isFarm,
    //         name: data.model.name || '',
    //         address: data.model.address ? `${data.model.address}, ${data.model.city}` : '',
    //         industryCode: data.model.industrycode || '',
    //       });
    //     }
    //   });
    // } else {
    //   this.editPropertyForm.patchValue({ name: '', address: '' });
    // }
  }

  onChrNumberChanged(number: number) {
    if (number > 11111 && number.toString().length > 5) {
      this.getChrInformationSub$ = this.propertiesService.getChrInformation(number).subscribe((data) => {
        if (data?.success) {
          const address = data.model.ejendom.byNavn || data.model.ejendom.postDistrikt;
          this.editPropertyForm.patchValue({
            address: `${data.model.ejendom.adresse}, ${address}`,
          });
        }
      });
    }
  }

  get isDisabledSaveButton(): boolean {
    return this.editPropertyForm.invalid;
  }

  ngOnDestroy(): void {}
}
