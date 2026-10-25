import { Component, EventEmitter, OnDestroy, OnInit,
  inject
} from '@angular/core';
import {FormBuilder, FormGroup, Validators, ReactiveFormsModule} from '@angular/forms';
import { applicationLanguages, applicationLanguagesTranslated } from 'src/app/common/const';
import { PropertyCreateModel } from '../../../../models';
import { AuthStateService } from 'src/app/common/store';
import { BackendConfigurationPnPropertiesService } from '../../../../services';
import {MatDialogRef, MatDialogTitle, MatDialogContent, MatDialogActions} from '@angular/material/dialog';
import { AutoUnsubscribe } from 'ngx-auto-unsubscribe';
import { Subscription } from 'rxjs';
import { AppSettingsStateService } from 'src/app/modules/application-settings/components/store';
import { ToastrService } from 'ngx-toastr';
import {TranslateService, TranslatePipe} from '@ngx-translate/core';
import {NgIf, AsyncPipe} from '@angular/common';
import { Store } from '@ngrx/store';
import { selectAuthIsAdmin } from 'src/app/state/auth/auth.selector';
import {MatFormField, MatLabel} from '@angular/material/form-field';
import {MatInput} from '@angular/material/input';
import {MatSlideToggle} from '@angular/material/slide-toggle';

@AutoUnsubscribe()
@Component({
  selector: 'app-property-create-modal',
  imports: [NgIf, AsyncPipe, ReactiveFormsModule, TranslatePipe, MatFormField, MatLabel, MatInput, MatDialogTitle, MatDialogContent, MatDialogActions, MatSlideToggle],
  templateUrl: './property-create-modal.component.html',
  styleUrls: ['./property-create-modal.component.scss'],
})
export class PropertyCreateModalComponent implements OnInit, OnDestroy {
  private fb = inject(FormBuilder);
  public authStateService = inject(AuthStateService);
  private propertiesService = inject(BackendConfigurationPnPropertiesService);
  public dialogRef = inject(MatDialogRef<PropertyCreateModalComponent>);
  private appSettingsStateService = inject(AppSettingsStateService);
  private toastr = inject(ToastrService);
  private translate = inject(TranslateService);
  private store = inject(Store);
  /** The Halebid toggle is for admins, as in the edit dialog. */
  public selectAuthIsAdmin$ = this.store.select(selectAuthIsAdmin);

  propertyCreate: EventEmitter<PropertyCreateModel> = new EventEmitter<PropertyCreateModel>();
  newPropertyForm: FormGroup;
  newProperty: PropertyCreateModel = new PropertyCreateModel();
  selectedLanguages: { id: number; checked: boolean }[] = [];
  propertyIsFarm: boolean = false;

  getCompanyTypeSub$: Subscription;
  getChrInformationSub$: Subscription;
  getLanguagesSub$: Subscription;
  /**
   * SDK Languages.Id by language code. SDK ids are AUTO_INCREMENT values and differ per
   * install (#8141), so the app-locale ids are mapped by code. Null until loaded; Create
   * stays disabled meanwhile, because a positional id would store the wrong language.
   */
  private sdkLanguageIdByCode: Map<string, number> | null = null;

  get applicationLanguages() {
    return applicationLanguages;
  }

  

  ngOnDestroy(): void {
  }

  ngOnInit() {
    this.propertyIsFarm = false;
    this.getLanguagesSub$ = this.appSettingsStateService.getLanguages().subscribe({
      next: res => {
        if (res?.success) {
          this.sdkLanguageIdByCode = new Map((res.model?.languages ?? []).map(l => [l.languageCode, l.id]));
        } else {
          this.onLanguagesFailed();
        }
      },
      error: () => this.onLanguagesFailed(),
    });

    // Initialize reactive form
    this.newPropertyForm = this.fb.group({
      cvr: ['', Validators.required],
      mainMailAddress: [''],
      name: ['', Validators.required],
      chr: [''],
      address: [''],
      workorderEnable: [false],
      tailBiteEnabled: [false],
      isFarm: [false]
    });

  }

  hide() {
    this.dialogRef.close();
    this.newPropertyForm.reset();
    this.selectedLanguages = [];
    this.propertyIsFarm = false;
  }

  private onLanguagesFailed() {
    this.toastr.error(this.translate.instant('Could not load languages'));
  }

  onCreateProperty() {
    if (this.isDisabledSaveButton) {
      return;
    }
    const newProperty: PropertyCreateModel = {
      ...this.newPropertyForm.value,
      // Only languages this install has; never a positional app-locale id.
      languagesIds: applicationLanguagesTranslated
        .map(x => this.sdkLanguageIdByCode.get(x.locale))
        .filter((id): id is number => id !== undefined)
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
    return this.newPropertyForm.invalid || !this.sdkLanguageIdByCode;
  }
}
