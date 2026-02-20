import {
  Component,
  EventEmitter,
  OnDestroy,
  OnInit, Output,
  inject
} from '@angular/core';
import {AutoUnsubscribe} from 'ngx-auto-unsubscribe';
import {Subscription} from 'rxjs';
import {CommonDictionaryModel, LanguagesModel} from 'src/app/common/models';
import {PropertyAssignmentWorkerModel, DeviceUserModel} from '../../../../models';
import {BackendConfigurationPnPropertiesService} from '../../../../services';
import {AuthStateService} from 'src/app/common/store';
import {MAT_DIALOG_DATA, MatDialogRef} from '@angular/material/dialog';
import {MtxGridColumn} from '@ng-matero/extensions/grid';
import {TranslateService} from '@ngx-translate/core';
import {tap} from 'rxjs/operators';
import {AppSettingsStateService} from 'src/app/modules/application-settings/components/store';
import {TimePlanningPnSettingsService} from 'src/app/plugins/modules/time-planning-pn/services';
import {
  AbstractControl,
  FormBuilder,
  FormControl,
  FormGroup,
  ValidationErrors,
  ValidatorFn,
  Validators
} from '@angular/forms';
import validator from 'validator';
import {AssignedSiteModel, GlobalAutoBreakSettingsModel} from 'src/app/plugins/modules/time-planning-pn/models';
import {Store} from '@ngrx/store';
import {selectAuthIsAdmin, selectCurrentUserIsFirstUser} from 'src/app/state';

@AutoUnsubscribe()
@Component({
    selector: 'app-property-worker-create-edit-modal',
    templateUrl: './property-worker-create-edit-modal.component.html',
    styleUrls: ['./property-worker-create-edit-modal.component.scss'],
    standalone: false
})
export class PropertyWorkerCreateEditModalComponent implements OnInit, OnDestroy {
  private store = inject(Store);
  private fb = inject(FormBuilder);
  public propertiesService = inject(BackendConfigurationPnPropertiesService);
  public authStateService = inject(AuthStateService);
  private translateService = inject(TranslateService);
  public dialogRef = inject(MatDialogRef<PropertyWorkerCreateEditModalComponent>);
  private appSettingsStateService = inject(AppSettingsStateService);
  private timePlanningPnSettingsService = inject(TimePlanningPnSettingsService);
  private model = inject<{
    deviceUser: DeviceUserModel,
    assignments: PropertyAssignmentWorkerModel[],
    availableProperties: CommonDictionaryModel[],
    availableTags: CommonDictionaryModel[],
    alreadyUsedEmails: string[];
  }>(MAT_DIALOG_DATA);
  protected selectAuthIsAdmin$ = this.store.select(selectAuthIsAdmin);
  public selectCurrentUserIsFirstUser$ = this.store.select(selectCurrentUserIsFirstUser);

  availableProperties: CommonDictionaryModel[] = [];
  edit: boolean = false;
  selectedDeviceUser: DeviceUserModel = new DeviceUserModel();
  selectedAssignedSite: AssignedSiteModel = new AssignedSiteModel();
  selectedDeviceUserCopy: DeviceUserModel = new DeviceUserModel();
  assignments: PropertyAssignmentWorkerModel[] = [];
  assignmentsCopy: PropertyAssignmentWorkerModel[] = [];
  taskManagementEnabled: boolean = false;
  timeRegistrationEnabled: boolean = false;
  availableTags: CommonDictionaryModel[] = [];
  alreadyUsedEmails: string[] = [];
  @Output() userUpdated: EventEmitter<void> = new EventEmitter<void>();
  tableHeaders: MtxGridColumn[] = [
    {
      header: this.translateService.stream('ID'),
      field: 'id',
    },
    {
      header: this.translateService.stream('Property name'),
      field: 'name',
      class: 'propertyName',
    },
    {
      header: this.translateService.stream('Select'),
      field: 'select',
    },
  ];

  deviceUserCreate$: Subscription;
  deviceUserUpdate$: Subscription;
  deviceUserAssign$: Subscription;
  getLanguagesSub$: Subscription;
  appLanguages: LanguagesModel = new LanguagesModel();
  activeLanguages: Array<any> = [];
  form: FormGroup;
  private globalAutoBreakSettings: GlobalAutoBreakSettingsModel;


  private updateDisabledFieldsBasedOnResigned() {
    const isResigned = this.form.get('resigned')?.value;
    Object.keys(this.form.controls).forEach(key => {
      if (key !== 'resigned' && key !== 'resignedAtDate') {
        if (isResigned) {
          this.form.get(key)?.disable({emitEvent: false});
        } else {
          this.form.get(key)?.enable({emitEvent: false});
        }
      }
    });
  }

  get languages() {
    return this.appLanguages.languages.filter((x) => x.isActive);
  }

  // Add this method to your component
  updateFormControlDisabledStates() {
    // userFirstName and userLastName
    // if (this.selectedDeviceUser.isBackendUser) {
    //   this.form.get('userFirstName')?.disable();
    //   this.form.get('userLastName')?.disable();
    // } else {
    //   this.form.get('userFirstName')?.enable();
    //   this.form.get('userLastName')?.enable();
    // }

    // languageCode
    const shouldDisableLanguage =
      this.selectedDeviceUser.resigned ||
      this.timeRegistrationEnabled ||
      this.taskManagementEnabled ||
      this.getAssignmentCount() > 0;
    if (shouldDisableLanguage) {
      this.form.get('languageCode')?.disable();
    } else {
      this.form.get('languageCode')?.enable();
    }

    // taskManagementEnabled (mat-slide-toggle)
    if (this.selectedDeviceUser.hasWorkOrdersAssigned) {
      this.form.get('taskManagementEnabled')?.disable();
      this.form.get('resigned').disable();
    } else {
      if (this.selectedDeviceUser.resigned) {
        this.form.get('taskManagementEnabled')?.disable();
      } else {
        this.form.get('taskManagementEnabled')?.enable();
      }
    }
  }

  ngOnInit() {
    this.assignments = [...this.model.assignments];
    this.availableProperties = [...this.model.availableProperties];
    this.selectedDeviceUser = {...this.model.deviceUser ?? new DeviceUserModel()};
    this.selectedDeviceUserCopy = {...this.model.deviceUser};
    this.assignmentsCopy = [...this.model.assignments];
    this.taskManagementEnabled = this.selectedDeviceUserCopy.taskManagementEnabled;
    this.timeRegistrationEnabled = this.selectedDeviceUserCopy.timeRegistrationEnabled;
    this.availableTags = [...this.model.availableTags];
    this.alreadyUsedEmails = [...this.model.alreadyUsedEmails];

    this.form = this.fb.group({
      userFirstName: [this.selectedDeviceUser.userFirstName || '', Validators.required],
      userLastName: [this.selectedDeviceUser.userLastName || '', Validators.required],
      workerEmail: [this.selectedDeviceUser.workerEmail || '', [
        Validators.required,
        (control) => validator.isEmail(control.value) ? null : {invalidEmail: true}
      ]],
      phoneNumber: [this.selectedDeviceUser.phoneNumber || '', [
        (control) => {
          const value = control.value;
          if (!value) {
            return null;
          }
          return validator.isMobilePhone(value) ? null : {invalidPhoneNumber: true};
        }
      ]],
      pinCode: [this.selectedDeviceUser.pinCode || ''],
      employeeNo: [this.selectedDeviceUser.employeeNo || ''],
      languageCode: [this.selectedDeviceUser.languageCode || ''],
      tags: [this.selectedDeviceUser.tags || []],
      timeRegistrationEnabled: [this.selectedDeviceUser.timeRegistrationEnabled || false],
      enableMobileAccess: [this.selectedDeviceUser.enableMobileAccess || false],
      taskManagementEnabled: [this.selectedDeviceUser.taskManagementEnabled || false],
      webAccessEnabled: [this.selectedDeviceUser.webAccessEnabled || false],
      archiveEnabled: [this.selectedDeviceUser.archiveEnabled || false],
      resigned: [this.selectedDeviceUser.resigned || false],
      resignedAtDate: [
        this.selectedDeviceUser.resigned ? new Date(this.selectedDeviceUser.resignedAtDate) : new Date(),
        this.selectedDeviceUser.resigned ? Validators.required : null
      ],
      // Time registration fields for each day of the week
      startMonday: [this.selectedDeviceUser.startMonday || null],
      endMonday: [this.selectedDeviceUser.endMonday || null],
      breakMonday: [this.selectedDeviceUser.breakMonday || null],
      startTuesday: [this.selectedDeviceUser.startTuesday || null],
      endTuesday: [this.selectedDeviceUser.endTuesday || null],
      breakTuesday: [this.selectedDeviceUser.breakTuesday || null],
      startWednesday: [this.selectedDeviceUser.startWednesday || null],
      endWednesday: [this.selectedDeviceUser.endWednesday || null],
      breakWednesday: [this.selectedDeviceUser.breakWednesday || null],
      startThursday: [this.selectedDeviceUser.startThursday || null],
      endThursday: [this.selectedDeviceUser.endThursday || null],
      breakThursday: [this.selectedDeviceUser.breakThursday || null],
      startFriday: [this.selectedDeviceUser.startFriday || null],
      endFriday: [this.selectedDeviceUser.endFriday || null],
      breakFriday: [this.selectedDeviceUser.breakFriday || null],
      startSaturday: [this.selectedDeviceUser.startSaturday || null],
      endSaturday: [this.selectedDeviceUser.endSaturday || null],
      breakSaturday: [this.selectedDeviceUser.breakSaturday || null],
      startSunday: [this.selectedDeviceUser.startSunday || null],
      endSunday: [this.selectedDeviceUser.endSunday || null],
      breakSunday: [this.selectedDeviceUser.breakSunday || null],
      // Time registration specific settings



      useGoogleSheetAsDefault: false,
      useOnlyPlanHours: false,
      autoBreakCalculationActive: false,
      allowPersonalTimeRegistration: false,
      allowEditOfRegistrations: false,
      usePunchClock: false,
      usePunchClockWithAllowRegisteringInHistory: false,
      allowAcceptOfPlannedHours: false,
      daysBackInTimeAllowedEditingEnabled: false,
      daysBackInTimeAllowedEditing: 2,
      thirdShiftActive: false,
      fourthShiftActive: false,
      fifthShiftActive: false,
      isManager: false,
      managingTagIds: []
    });

    // Build autoBreakSettings form group
    const days = ['monday', 'tuesday', 'wednesday', 'thursday', 'friday', 'saturday', 'sunday'];
    const autoBreakGroup = days.reduce((acc, day) => {
      acc[day] = this.fb.group({
        breakMinutesDivider: new FormControl(null),
        breakMinutesPrDivider: new FormControl(null),
        breakMinutesUpperLimit: new FormControl(null),
      });
      return acc;
    }, {} as { [key: string]: FormGroup });
    this.form.addControl('autoBreakSettings', this.fb.group(autoBreakGroup));

    // Fetch global auto break calculation settings
    this.timePlanningPnSettingsService.getGlobalAutoBreakCalculationSettings().subscribe(result => {
      if (result && result.success) {
        this.globalAutoBreakSettings = result.model;
      }
    });

    this.timeRegistrationEnabled ? this.timePlanningPnSettingsService.getAssignedSite(this.selectedDeviceUser.id).pipe(
      tap((response) => {
        if (response && response.success && response.model) {
          this.selectedAssignedSite = response.model;
            this.form.patchValue({
              useGoogleSheetAsDefault: this.selectedAssignedSite.useGoogleSheetAsDefault || false,
              useOnlyPlanHours: this.selectedAssignedSite.useOnlyPlanHours || false,
              autoBreakCalculationActive: this.selectedAssignedSite.autoBreakCalculationActive || false,
              allowPersonalTimeRegistration: this.selectedAssignedSite.allowPersonalTimeRegistration || false,
              allowEditOfRegistrations: this.selectedAssignedSite.allowEditOfRegistrations || false,
              usePunchClock: this.selectedAssignedSite.usePunchClock || false,
              usePunchClockWithAllowRegisteringInHistory: this.selectedAssignedSite.usePunchClockWithAllowRegisteringInHistory || false,
              allowAcceptOfPlannedHours: this.selectedAssignedSite.allowAcceptOfPlannedHours || false,
              daysBackInTimeAllowedEditingEnabled: this.selectedAssignedSite.daysBackInTimeAllowedEditingEnabled || false,
              daysBackInTimeAllowedEditing: this.selectedAssignedSite.daysBackInTimeAllowedEditing || 2,
              thirdShiftActive: this.selectedAssignedSite.thirdShiftActive || false,
              fourthShiftActive: this.selectedAssignedSite.fourthShiftActive || false,
              fifthShiftActive: this.selectedAssignedSite.fifthShiftActive || false,
              isManager: this.selectedAssignedSite.isManager || false,
              managingTagIds: this.selectedAssignedSite.managingTagIds || [],
            });

            // Patch auto break settings from assigned site
            const autoBreakFg = this.form.get('autoBreakSettings') as FormGroup;
            const days = ['monday', 'tuesday', 'wednesday', 'thursday', 'friday', 'saturday', 'sunday'];
            days.forEach(day => {
              const dayGroup = autoBreakFg.get(day) as FormGroup;
              if (dayGroup) {
                dayGroup.patchValue({
                  breakMinutesDivider: this.getConvertedValue(this.selectedAssignedSite[`${day}BreakMinutesDivider`]) ?? null,
                  breakMinutesPrDivider: this.getConvertedValue(this.selectedAssignedSite[`${day}BreakMinutesPrDivider`]) ?? null,
                  breakMinutesUpperLimit: this.getConvertedValue(this.selectedAssignedSite[`${day}BreakMinutesUpperLimit`]) ?? null,
                });
              }
            });
        }
      })
    ).subscribe() : null;

    if (this.selectedDeviceUser.resigned) {
      Object.keys(this.form.controls).forEach(key => {
        if (key !== 'resigned' && key !== 'resignedAtDate') {
          this.form.get(key)?.disable();
        }
      });
    }

    this.form.valueChanges.subscribe(formValue => {
      Object.assign(this.selectedDeviceUser, formValue);
    });

    this.form.get('enableMobileAccess')?.valueChanges.subscribe(enabled => {
      const emailControl = this.form.get('workerEmail');
      const currentEmail = emailControl?.value || '';

      if (enabled && currentEmail.includes('invalid')) {
        emailControl?.patchValue('');
        emailControl?.markAsTouched();
      }

      this.updateEmailValidation();
    });

    this.form.get('webAccessEnabled')?.valueChanges.subscribe(enabled => {
      const emailControl = this.form.get('workerEmail');
      const currentEmail = emailControl?.value || '';

      if (enabled && currentEmail.includes('invalid')) {
        emailControl?.patchValue('');
        emailControl?.markAsTouched();
      }
      this.updateEmailValidation();
    });

    this.form.get('archiveEnabled')?.valueChanges.subscribe(enabled => {
      const emailControl = this.form.get('workerEmail');
      const currentEmail = emailControl?.value || '';

      if (enabled && currentEmail.includes('invalid')) {
        emailControl?.patchValue('');
        emailControl?.markAsTouched();
      }
      this.updateEmailValidation();
    });

    this.updateEmailValidation();

    this.updateDisabledFieldsBasedOnResigned();

    this.form.get('resigned')?.valueChanges.subscribe(() => {
      this.updateDisabledFieldsBasedOnResigned();
    });

    this.getEnabledLanguages();
    this.updateFormControlDisabledStates();
  }

  hide(result = false) {
    this.selectedDeviceUser = new DeviceUserModel();
    this.assignments = [];
    this.dialogRef.close(result);
  }

  addToArray(e: any, propertyId: number) {
    const assignmentObject = new PropertyAssignmentWorkerModel();
    if (e.checked) {
      assignmentObject.isChecked = true;
      assignmentObject.propertyId = propertyId;
      this.assignments = [...this.assignments, assignmentObject];
    } else {
      this.assignments = this.assignments.filter(
        (x) => x.propertyId !== propertyId
      );
    }
  }

  getAssignmentIsCheckedByPropertyId(propertyId: number): boolean {
    const assignment = this.assignments.find(
      (x) => x.propertyId === propertyId
    );
    return assignment ? assignment.isChecked : false;
  }

  getAssignmentIsLockedByPropertyId(propertyId: number): boolean {
    if (this.selectedDeviceUser.resigned) {
      return true;
    }
    const assignment = this.assignments.find(
      (x) => x.propertyId === propertyId
    );
    if (assignment) {
      if (assignment.isLocked) {
        this.form.get('resigned').disable();
      }
    }
    return assignment ? assignment.isLocked : false;
  }

  updateSingle() {
    if (this.form.invalid) {
      return;
    }
    const formValue = this.form.value;
    Object.assign(this.selectedDeviceUser, formValue);
    Object.assign(this.selectedAssignedSite, formValue);

    // Map auto break settings from nested form group to flat model properties
    if (formValue.autoBreakSettings) {

      formValue.autoBreakSettings.monday.breakMinutesDivider =
        this.convertStringToMinutes(formValue.autoBreakSettings.monday.breakMinutesDivider as string);
      formValue.autoBreakSettings.monday.breakMinutesPrDivider =
        this.convertStringToMinutes(formValue.autoBreakSettings.monday.breakMinutesPrDivider as string);
      formValue.autoBreakSettings.monday.breakMinutesUpperLimit =
        this.convertStringToMinutes(formValue.autoBreakSettings.monday.breakMinutesUpperLimit as string);
      formValue.autoBreakSettings.tuesday.breakMinutesDivider =
        this.convertStringToMinutes(formValue.autoBreakSettings.tuesday.breakMinutesDivider as string);
      formValue.autoBreakSettings.tuesday.breakMinutesPrDivider =
        this.convertStringToMinutes(formValue.autoBreakSettings.tuesday.breakMinutesPrDivider as string);
      formValue.autoBreakSettings.tuesday.breakMinutesUpperLimit =
        this.convertStringToMinutes(formValue.autoBreakSettings.tuesday.breakMinutesUpperLimit as string);
      formValue.autoBreakSettings.wednesday.breakMinutesDivider =
        this.convertStringToMinutes(formValue.autoBreakSettings.wednesday.breakMinutesDivider as string);
      formValue.autoBreakSettings.wednesday.breakMinutesPrDivider =
        this.convertStringToMinutes(formValue.autoBreakSettings.wednesday.breakMinutesPrDivider as string);
      formValue.autoBreakSettings.wednesday.breakMinutesUpperLimit =
        this.convertStringToMinutes(formValue.autoBreakSettings.wednesday.breakMinutesUpperLimit as string);
      formValue.autoBreakSettings.thursday.breakMinutesDivider =
        this.convertStringToMinutes(formValue.autoBreakSettings.thursday.breakMinutesDivider as string);
      formValue.autoBreakSettings.thursday.breakMinutesPrDivider =
        this.convertStringToMinutes(formValue.autoBreakSettings.thursday.breakMinutesPrDivider as string);
      formValue.autoBreakSettings.thursday.breakMinutesUpperLimit =
        this.convertStringToMinutes(formValue.autoBreakSettings.thursday.breakMinutesUpperLimit as string);
      formValue.autoBreakSettings.friday.breakMinutesDivider =
        this.convertStringToMinutes(formValue.autoBreakSettings.friday.breakMinutesDivider as string);
      formValue.autoBreakSettings.friday.breakMinutesPrDivider =
        this.convertStringToMinutes(formValue.autoBreakSettings.friday.breakMinutesPrDivider as string);
      formValue.autoBreakSettings.friday.breakMinutesUpperLimit =
        this.convertStringToMinutes(formValue.autoBreakSettings.friday.breakMinutesUpperLimit as string);
      formValue.autoBreakSettings.saturday.breakMinutesDivider =
        this.convertStringToMinutes(formValue.autoBreakSettings.saturday.breakMinutesDivider as string);
      formValue.autoBreakSettings.saturday.breakMinutesPrDivider =
        this.convertStringToMinutes(formValue.autoBreakSettings.saturday.breakMinutesPrDivider as string);
      formValue.autoBreakSettings.saturday.breakMinutesUpperLimit =
        this.convertStringToMinutes(formValue.autoBreakSettings.saturday.breakMinutesUpperLimit as string);
      formValue.autoBreakSettings.sunday.breakMinutesDivider =
        this.convertStringToMinutes(formValue.autoBreakSettings.sunday.breakMinutesDivider as string);
      formValue.autoBreakSettings.sunday.breakMinutesPrDivider =
        this.convertStringToMinutes(formValue.autoBreakSettings.sunday.breakMinutesPrDivider as string);
      formValue.autoBreakSettings.sunday.breakMinutesUpperLimit =
        this.convertStringToMinutes(formValue.autoBreakSettings.sunday.breakMinutesUpperLimit as string);
      const days = ['monday', 'tuesday', 'wednesday', 'thursday', 'friday', 'saturday', 'sunday'];
      days.forEach(day => {
        debugger;
        const daySettings = formValue.autoBreakSettings[day];
        if (daySettings) {
          this.selectedAssignedSite[`${day}BreakMinutesDivider`] = this.timeToMinutes(daySettings.breakMinutesDivider) ?? 0;
          this.selectedAssignedSite[`${day}BreakMinutesPrDivider`] = this.timeToMinutes(daySettings.breakMinutesPrDivider) ?? 0;
          this.selectedAssignedSite[`${day}BreakMinutesUpperLimit`] = this.timeToMinutes(daySettings.breakMinutesUpperLimit) ?? 0;
        }
      });
    }
    this.selectedDeviceUser.siteUid = this.selectedDeviceUser.id;
    this.deviceUserCreate$ = this.propertiesService
      .updateSingleDeviceUser(this.selectedDeviceUser)
      .subscribe((operation) => {
        if (operation && operation.success && this.assignments) {
          if (this.timeRegistrationEnabled) {
            this.timePlanningPnSettingsService.updateAssignedSite(this.selectedAssignedSite).subscribe(result => {
              if (result && result.success) {
                this.assignWorkerToPropertiesUpdate();
              }
            });
          } else {
            this.assignWorkerToPropertiesUpdate();
          }
        } else {
          this.hide(true);
        }
      });
  }

  convertStringToMinutes(time: string): number {
    const [hours, minutes] = time.split(':').map(Number);
    const result = hours * 60 + minutes;
    if (isNaN(result)) {
      return 0;
    }
    return result;
  }

  createDeviceUser() {
    if (this.form.invalid) {
      return;
    }
    const formValue = this.form.value;
    Object.assign(this.selectedDeviceUser, formValue);
    this.deviceUserCreate$ = this.propertiesService
      .createSingleDeviceUser(this.selectedDeviceUser)
      .subscribe((operation) => {
        if (operation && operation.success) {
          if (this.assignments && this.assignments.length > 0) {
            this.assignWorkerToProperties(operation.model);
          } else {
            this.hide(true);
          }
        }
      });
  }

  assignWorkerToProperties(siteId: number) {
    this.deviceUserAssign$ = this.propertiesService
      .assignPropertiesToWorker({
        siteId,
        assignments: this.assignments,
        // eslint-disable-next-line max-len
        timeRegistrationEnabled: this.form.value.timeRegistrationEnabled === undefined ? this.selectedDeviceUser.timeRegistrationEnabled : this.form.value.timeRegistrationEnabled,
        // eslint-disable-next-line max-len
        taskManagementEnabled: this.form.value.taskManagementEnabled === undefined ? this.selectedDeviceUser.taskManagementEnabled : this.form.value.taskManagementEnabled,
      })
      .subscribe((operation) => {
        if (operation && operation.success) {
          this.hide(true);
        }
      });
  }

  assignWorkerToPropertiesUpdate() {
    this.deviceUserAssign$ = this.propertiesService
      .updateAssignPropertiesToWorker({
        siteId: this.selectedDeviceUser.normalId,
        assignments: this.assignments,
        // eslint-disable-next-line max-len
        timeRegistrationEnabled: this.form.value.timeRegistrationEnabled === undefined ? this.selectedDeviceUser.timeRegistrationEnabled : this.form.value.timeRegistrationEnabled,
        // eslint-disable-next-line max-len
        taskManagementEnabled: this.form.value.taskManagementEnabled === undefined ? this.selectedDeviceUser.taskManagementEnabled : this.form.value.taskManagementEnabled,
      })
      .subscribe((operation) => {
        if (operation && operation.success) {
          this.hide(true);
        }
      });
  }

  getAssignmentByPropertyId(propertyId: number): PropertyAssignmentWorkerModel {
    return (
      this.assignments.find((x) => x.propertyId === propertyId) ?? {
        propertyId: propertyId,
        isChecked: false,
        isLocked: false,
      }
    );
  }

  getAssignmentCount(): number {
    return this.assignmentsCopy.filter((x) => x.isChecked).length;
  }

  ngOnDestroy(): void {
  }

  getEnabledLanguages() {
    this.getLanguagesSub$ = this.appSettingsStateService.getLanguages()
      .pipe(tap(data => {
        if (data && data.success && data.model) {
          this.appLanguages = data.model;
          this.activeLanguages = this.appLanguages.languages.filter((x) => x.isActive);
          if (this.selectedDeviceUser.id) {
            this.edit = true;
          }
          if (!this.edit) {
            this.form.patchValue({languageCode: this.languages[0].languageCode});
            if (this.authStateService.checkClaim('task_management_enable')) {
              this.form.patchValue({taskManagementEnabled: false});
            }
          }
        }
      }))
      .subscribe();
  }

  generateRandomEmail(): void {
    const firstName = this.form.get('userFirstName')?.value?.toLowerCase().trim() || 'user';
    const lastName = this.form.get('userLastName')?.value?.toLowerCase().trim() || 'name';
    let email: string;
    let randomNumber: number;

    do {
      randomNumber = Math.floor(Math.random() * (100000 - 1000 + 1)) + 1000;
      email = `${firstName}_${lastName}_${randomNumber}_invalid@microting.com`;
    } while (this.alreadyUsedEmails.includes(email));

    this.form.patchValue({
      workerEmail: email
    });
  }

  shouldShowGenerateEmailButton(): boolean {
    const enableMobileAccess = !this.form?.get('enableMobileAccess')?.value;
    const webAccessEnabled = !this.form?.get('webAccessEnabled')?.value;
    const archiveEnabled = !this.form?.get('archiveEnabled')?.value;
    return enableMobileAccess && webAccessEnabled && archiveEnabled;
  }

  private updateEmailValidation(): void {
    const emailControl = this.form.get('workerEmail');
    const enableMobileAccess = this.form.get('enableMobileAccess')?.value;
    const webAccessEnabled = this.form.get('webAccessEnabled')?.value;
    const archiveEnabled = this.form.get('archiveEnabled')?.value;

    if (enableMobileAccess || webAccessEnabled || archiveEnabled) {
      emailControl?.setValidators([Validators.required, Validators.email, this.validEmailValidator()]);
    } else {
      emailControl?.setValidators([Validators.required, this.validEmailValidator()]);
    }

    emailControl?.updateValueAndValidity();
  }

  private validEmailValidator(): ValidatorFn {
    return (control: AbstractControl): ValidationErrors | null => {
      const email = control.value;
      const enableMobileAccess = this.form?.get('enableMobileAccess')?.value;
      const webAccessEnabled = this.form.get('webAccessEnabled')?.value;
      const archiveEnabled = this.form.get('archiveEnabled')?.value;

      if ((enableMobileAccess || webAccessEnabled || archiveEnabled) && email && email.includes('invalid')) {
        return {invalidEmail: true};
      }
      return null;
    };
  }

  // Helper methods for time registration - convert minutes from midnight to HH:mm format
  minutesToTime(minutes: number | null): string {
    if (minutes === null || minutes === undefined) {
      return '';
    }
    const hours = Math.floor(minutes / 60);
    const mins = minutes % 60;
    return `${hours.toString().padStart(2, '0')}:${mins.toString().padStart(2, '0')}`;
  }

  // Convert HH:mm format to minutes from midnight
  timeToMinutes(time: string): number | null {
    if (!time) {
      return null;
    }
    const [hours, minutes] = time.split(':').map(Number);
    return hours * 60 + minutes;
  }

  getConvertedValue(minutes: number, compareMinutes?: number): string {
    if (minutes === null || minutes === undefined) {
      return '';
    }
    const hours = Math.floor(minutes / 60);
    const mins = minutes % 60;
    let result = `${this.padZero(hours)}:${this.padZero(mins)}`;
    if (result === '00:00' && (compareMinutes === 0 || compareMinutes === undefined || compareMinutes === null)) {
      result = '';
    }
    return result;
  }

  padZero(num: number): string {
    return num < 10 ? `0${num}` : `${num}`;
  }

  setAutoBreakValue(day: string, control: string, value: string) {
    const fg = this.form.get('autoBreakSettings') as FormGroup;
    fg.get(day)?.get(control)?.setValue(value, {emitEvent: true});
  }

  getAutoBreakSettingsFormGroup(): FormGroup {
    return this.form.get('autoBreakSettings') as FormGroup;
  }

  copyBreakSettings(day: string) {
    if (!this.globalAutoBreakSettings) {
      return;
    }

    const fg = this.form.get('autoBreakSettings') as FormGroup;
    const dayGroup = fg.get(day) as FormGroup;

    dayGroup.patchValue({
      breakMinutesDivider: this.getConvertedValue(this.globalAutoBreakSettings[`${day}BreakMinutesDivider`]),
      breakMinutesPrDivider: this.getConvertedValue(this.globalAutoBreakSettings[`${day}BreakMinutesPrDivider`]),
      breakMinutesUpperLimit: this.getConvertedValue(this.globalAutoBreakSettings[`${day}BreakMinutesUpperLimit`]),
    });
  }
}
