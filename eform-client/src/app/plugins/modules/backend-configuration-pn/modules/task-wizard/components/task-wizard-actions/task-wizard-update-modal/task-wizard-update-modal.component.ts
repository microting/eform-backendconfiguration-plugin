import {ChangeDetectorRef, Component, EventEmitter, OnDestroy, OnInit,
  inject
} from '@angular/core';
import {AutoUnsubscribe} from 'ngx-auto-unsubscribe';
import {
  CommonDictionaryModel,
  FolderDto,
  LanguagesModel,
  SharedTagModel,
  TemplateDto,
  TemplateListModel,
  TemplateRequestModel
} from 'src/app/common/models';
import {FormBuilder, FormGroup, FormControl, Validators} from '@angular/forms';
import {RepeatTypeEnum, TaskWizardStatusesEnum} from '../../../../../enums';
import {TaskWizardCreateModel} from '../../../../../models';
import {TranslateService} from '@ngx-translate/core';
import {MatDialog, MatDialogRef} from '@angular/material/dialog';
import {EFormService} from 'src/app/common/services';
import {debounceTime, switchMap} from 'rxjs/operators';
import {MatDatepickerInputEvent} from '@angular/material/datepicker';
import {set} from 'date-fns';
import {generateWeeksList} from '../../../../../helpers';
import * as R from 'ramda';
import {MtxGridColumn} from '@ng-matero/extensions/grid';
import {MatCheckboxChange} from '@angular/material/checkbox';
import {dialogConfigHelper, findFullNameById, fixTranslationsByLanguages} from 'src/app/common/helpers';
import {Subscription, take} from 'rxjs';
import {Overlay} from '@angular/cdk/overlay';
import {TaskWizardFoldersModalComponent} from '../';
import {PlanningTagsComponent} from '../../../../../../items-planning-pn/modules/plannings/components';
import {selectAuthIsAuth, selectCurrentUserIsAdmin} from 'src/app/state/auth/auth.selector';
import {Store} from '@ngrx/store';

@AutoUnsubscribe()
@Component({
  selector: 'app-task-wizard-update-modal',
  templateUrl: './task-wizard-update-modal.component.html',
  styleUrls: ['./task-wizard-update-modal.component.scss'],
  standalone: false
})
export class TaskWizardUpdateModalComponent implements OnInit, OnDestroy {
  private store = inject(Store);
  private translateService = inject(TranslateService);
  public dialogRef = inject(MatDialogRef<TaskWizardUpdateModalComponent>);
  private eFormService = inject(EFormService);
  private cd = inject(ChangeDetectorRef);
  public dialog = inject(MatDialog);
  private overlay = inject(Overlay);
  private fb = inject(FormBuilder);

  planningTagsModal: PlanningTagsComponent;
  updateTask: EventEmitter<TaskWizardCreateModel> = new EventEmitter<TaskWizardCreateModel>();
  typeahead = new EventEmitter<string>();
  changeProperty: EventEmitter<number> = new EventEmitter<number>();
  taskForm: FormGroup;
  properties: CommonDictionaryModel[] = [];
  tags: CommonDictionaryModel[] = [];
  sites: CommonDictionaryModel[] = [];
  repeatEveryArr: { id: number, name: string }[] = [];
  repeatTypeArr: { id: number, name: string }[] = [];
  statuses: { label: string, value: number }[] = [];
  foldersTreeDto: FolderDto[] = [];
  selectedFolderName: string = '';
  repeatTypeDay: { name: string, id: number }[] = [];
  repeatTypeWeek: { name: string, id: number }[] = [];
  repeatTypeMonth: { name: string, id: number }[] = [];
  dayOfWeekArr: { id: number, name: string }[];
  appLanguages: LanguagesModel = new LanguagesModel();
  templateRequestModel: TemplateRequestModel = new TemplateRequestModel();
  templatesModel: TemplateListModel = new TemplateListModel();
  tableHeaders: MtxGridColumn[] = [
    {field: 'id', header: this.translateService.stream('Id')},
    {field: 'name', header: this.translateService.stream('Task solver'),},
    {field: 'select', header: this.translateService.stream('Select'),},
  ];
  protected copyModel: TaskWizardCreateModel;
  public model: TaskWizardCreateModel = {
    eformId: null,
    folderId: null,
    propertyId: null,
    repeatEvery: null,
    repeatType: null,
    itemPlanningTagId: null,
    startDate: null,
    status: TaskWizardStatusesEnum.Active,
    sites: [],
    tagIds: [],
    translates: [],
    complianceEnabled: true
  };

  folderSelectedSub$: Subscription;
  public isAuth$ = this.store.select(selectAuthIsAuth);
  public selectCurrentUserIsAdmin$ = this.store.select(selectCurrentUserIsAdmin);

  get TaskWizardStatusesEnum() {
    return TaskWizardStatusesEnum;
  }

  get disabledSaveButton(): boolean {
    return R.equals(this.taskForm.value, this.copyModel);
  }

  
  constructor() {
    this.typeahead
      .pipe(
        debounceTime(200),
        switchMap((term) => {
          this.templateRequestModel.nameFilter = term;
          return this.eFormService.getAll(this.templateRequestModel);
        })
      )
      .subscribe((items) => {
        this.templatesModel = items.model;
        this.cd.markForCheck();
      });
  }


  ngOnInit(): void {
    // this.initForm();

    this.statuses = Object.keys(TaskWizardStatusesEnum)
      .filter(key => isNaN(Number(key)))
      .map(key => ({
        label: this.translateService.instant(key),
        value: TaskWizardStatusesEnum[key],
      }));

    const activeLanguages = (this.appLanguages.languages ?? [])
      .filter(x => x.isActive)
      .map(x => ({languageId: x.id}));

    this.model.translates = fixTranslationsByLanguages(this.model.translates, activeLanguages);

    this.repeatEveryArr = generateWeeksList(this.translateService, 52);
    this.repeatTypeArr = Object.keys(RepeatTypeEnum)
      .filter(key => isNaN(Number(key)))
      .map(key => ({
        name: this.translateService.instant(key),
        id: RepeatTypeEnum[key],
      }));

    this.repeatTypeDay = R.map(x => ({
      name: x === 1 ? this.translateService.instant('Every') : x.toString(),
      id: x
    }), R.range(1, 31));

    this.repeatTypeWeek = R.map(x => ({
      name: x === 1 ? this.translateService.instant('Every') : x.toString(),
      id: x
    }), R.range(1, 51));

    this.repeatTypeMonth = [
      {id: 1, name: this.translateService.instant('Every month')},
      {id: 2, name: this.translateService.instant('2nd months')},
      {id: 3, name: this.translateService.instant('3rd months')},
      {id: 6, name: this.translateService.instant('6th months')},
      {id: 12, name: this.translateService.instant('12 (1 year)')},
      {id: 24, name: this.translateService.instant('24 (2 years)')},
      {id: 36, name: this.translateService.instant('36 (3 years)')},
      {id: 48, name: this.translateService.instant('48 (4 years)')},
      {id: 60, name: this.translateService.instant('60 (5 years)')},
      {id: 72, name: this.translateService.instant('72 (6 years)')},
      {id: 84, name: this.translateService.instant('84 (7 years)')},
      {id: 96, name: this.translateService.instant('96 (8 years)')},
      {id: 108, name: this.translateService.instant('108 (9 years)')},
      {id: 120, name: this.translateService.instant('120 (10 years)')},
    ];

    this.dayOfWeekArr = [
      {id: 1, name: this.translateService.instant('Monday')},
      {id: 2, name: this.translateService.instant('Tuesday')},
      {id: 3, name: this.translateService.instant('Wednesday')},
      {id: 4, name: this.translateService.instant('Thursday')},
      {id: 5, name: this.translateService.instant('Friday')},
      {id: 6, name: this.translateService.instant('Saturday')},
      {id: 0, name: this.translateService.instant('Sunday')}
    ];
  }

  initForm() {
    this.taskForm = this.fb.group({
      taskStatus: [this.model?.status === TaskWizardStatusesEnum.Active],
      propertyId: [this.model?.propertyId, Validators.required],
      itemPlanningTagId: [this.model?.itemPlanningTagId],
      startDate: [this.model?.startDate],
      repeatType: [this.model?.repeatType ?? 0],
      repeatEvery: [this.model?.repeatEvery],
      eformId: [this.model?.eformId, Validators.required],
      tagIds: [this.model?.tagIds || []],
      sites: [this.model?.sites || []],
      folderId: [this.model?.folderId],
      complianceEnabled: [this.model?.complianceEnabled],
      translates: this.fb.array(
        this.model.translates.map(t => this.fb.group({
          languageId: [t.languageId],
          id: [t.id],
          name: [t.name || '', Validators.required],
          description: [t.description || '']
        }))
      )
    });
  }

  changePropertyId(property: CommonDictionaryModel) {
    this.taskForm.patchValue({
      propertyId: property.id,
      sites: [],
      folderId: null
    });
    this.changeProperty.emit(property.id);
    this.selectedFolderName = '';
  }

  changeTagIds(tags: SharedTagModel[]) {
    this.taskForm.patchValue({
      tagIds: tags ? tags.map((x) => x.id) : [],
    });
  }

  changePlanningTagId(tag: SharedTagModel) {
    this.taskForm.patchValue({
      itemPlanningTagId: tag ? tag.id : null,
    });
  }


  /*updateLanguageModel(translationsModel: CommonTranslationsModel, index: number) {
      this.model.translates[index] = translationsModel;
    }*/

  updateName(name: string, index: number) {
    this.model.translates[index].name = name;
  }

  updateEformId(event: any) {
    const eformId = event?.id ?? event;
    this.taskForm.patchValue({eformId: eformId});
  }

  updateStartDate(e: MatDatepickerInputEvent<any, any>) {
    if (!e?.value) {
      return;
    }
    const normalized = set(e.value, {hours: 0, minutes: 0, seconds: 0, milliseconds: 0});
    this.taskForm.patchValue({startDate: normalized});
  }

  changeRepeatType(event: any) {
    const typeId = event?.id ?? event;
    this.taskForm.patchValue({repeatType: typeId});
  }

  changeRepeatEvery(value: number) {
    this.taskForm.patchValue({repeatEvery: value});
  }

  changeStatus(event: boolean) {
    this.taskForm.patchValue({
      taskStatus: event,
    });
    this.model.status = event ? TaskWizardStatusesEnum.Active : TaskWizardStatusesEnum.NotActive;
  }

  getLanguageName(languageId: number): string {
    const lang = (this.appLanguages.languages ?? []).find(x => x.id === languageId);
    return lang ? lang.name : 'Unknown';
  }

  repeatTypeMass() {
    const repeatType = this.taskForm?.get('repeatType')?.value;
    switch (repeatType) {
      case RepeatTypeEnum.Day:
        return this.repeatTypeDay;
      case RepeatTypeEnum.Week:
        return this.repeatTypeWeek;
      case RepeatTypeEnum.Month:
        return this.repeatTypeMonth;
      default:
        return [];
    }
  }

  getAssignmentBySiteId(siteId: number): boolean {
    const index = this.taskForm.get('sites')?.value?.findIndex(
      (x) => x === siteId
    );
    return index !== -1;
  }

  addToArray(e: MatCheckboxChange, siteId: number) {
    const sites: number[] = [...(this.taskForm.get('sites')?.value || [])];
    if (e.checked && !sites.includes(siteId)) {
      sites.push(siteId);
    } else if (!e.checked && sites.includes(siteId)) {
      sites.splice(sites.indexOf(siteId), 1);
    }
    this.taskForm.patchValue({sites});
  }

  openFoldersModal() {
    if (this.taskForm.value.propertyId) {
      const foldersModal = this.dialog.open(TaskWizardFoldersModalComponent,
        {...dialogConfigHelper(this.overlay), hasBackdrop: true});
      foldersModal.backdropClick().pipe(take(1)).subscribe(_ => foldersModal.close());
      foldersModal.componentInstance.folders = this.foldersTreeDto;
      foldersModal.componentInstance.eFormSdkFolderId =
        this.taskForm.value.folderId ? this.taskForm.value.folderId : null;
      this.folderSelectedSub$ = foldersModal.componentInstance.folderSelected.subscribe(x => {
        this.taskForm.patchValue({folderId: x.id});
        this.selectedFolderName = findFullNameById(x.id, this.foldersTreeDto);
      });
    }
  }

  update() {

    const task: TaskWizardCreateModel = {
      ...this.taskForm.value,
      translates: this.taskForm.get('translates')?.value,
    };
    task.status = this.taskForm.value.taskStatus ? TaskWizardStatusesEnum.Active : TaskWizardStatusesEnum.NotActive;

    this.updateTask.emit(task);
  }

  openTagsModal() {
    this.planningTagsModal.show();
  }

  hide() {
    this.dialogRef.close();
  }

  fillModelAndCopyModel(model: TaskWizardCreateModel) {

    this.model = R.clone(model);
    this.initForm();

    const translatesFormArray = this.fb.array(
      (model.translates || []).map(t =>
        this.fb.group({
          languageId: [t.languageId],
          id: [t.id],
          name: [t.name || '', Validators.required],
          description: [t.description || ''],
        })
      )
    );
    this.taskForm.setControl('translates', translatesFormArray);

    this.copyModel = R.clone(model);

    this.selectedFolderName = model.folderId
      ? findFullNameById(model.folderId, this.foldersTreeDto)
      : '';

    this.cd.detectChanges();
  }


  ngOnDestroy(): void {
  }

  changeComplianceEnabled(checked: boolean) {
    this.taskForm.patchValue({
      complianceEnabled: checked,
    });
    this.model.complianceEnabled = !!checked;
  }
}
