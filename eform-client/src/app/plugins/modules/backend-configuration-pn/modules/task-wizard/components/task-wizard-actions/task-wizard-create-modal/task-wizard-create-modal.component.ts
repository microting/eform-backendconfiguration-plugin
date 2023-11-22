import {ChangeDetectorRef, Component, EventEmitter, OnDestroy, OnInit,} from '@angular/core';
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
import {TaskWizardFoldersModalComponent} from '../';
import {findFullNameById, dialogConfigHelper} from 'src/app/common/helpers';
import {Subscription, take} from 'rxjs';
import {Overlay} from '@angular/cdk/overlay';
import {PlanningTagsComponent} from 'src/app/plugins/modules/items-planning-pn/modules/plannings/components';
import {AuthStateService} from 'src/app/common/store';
import {selectAuthIsAuth} from 'src/app/state/auth/auth.selector';
import {Store} from '@ngrx/store';

@AutoUnsubscribe()
@Component({
  selector: 'app-task-wizard-create-modal',
  templateUrl: './task-wizard-create-modal.component.html',
  styleUrls: ['./task-wizard-create-modal.component.scss'],
})
export class TaskWizardCreateModalComponent implements OnInit, OnDestroy {
  planningTagsModal: PlanningTagsComponent
  createTask: EventEmitter<TaskWizardCreateModel> = new EventEmitter<TaskWizardCreateModel>();
  changeProperty: EventEmitter<number> = new EventEmitter<number>();
  typeahead = new EventEmitter<string>();
  properties: CommonDictionaryModel[] = [];
  tags: CommonDictionaryModel[] = [];
  sites: CommonDictionaryModel[] = [];
  foldersTreeDto: FolderDto[] = [];
  repeatEveryArr: { id: number, name: string }[] = [];
  repeatTypeArr: { id: number, name: string }[] = [];
  statuses: { label: string, value: number }[] = [];
  repeatTypeDay: { name: string, id: number }[] = [];
  repeatTypeWeek: { name: string, id: number }[] = [];
  repeatTypeMonth: { name: string, id: number }[] = [];
  dayOfWeekArr: { id: number, name: string }[] = [];
  selectedFolderName: string = '';
  appLanguages: LanguagesModel = new LanguagesModel();
  templateRequestModel: TemplateRequestModel = new TemplateRequestModel();
  templatesModel: TemplateListModel = new TemplateListModel();
  tableHeaders: MtxGridColumn[] = [
    {field: 'id', header: this.translateService.stream('Id')},
    {field: 'name', header: this.translateService.stream('Task solver'),},
    {field: 'select', header: this.translateService.stream('Select'),},
  ];
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
    translates: []
  };
  public isAuth$ = this.store.select(selectAuthIsAuth);

  private folderSelectedSub$: Subscription;

  get TaskWizardStatusesEnum() {
    return TaskWizardStatusesEnum;
  }

  constructor(
    private store: Store,
    private translateService: TranslateService,
    public dialogRef: MatDialogRef<TaskWizardCreateModalComponent>,
    private eFormService: EFormService,
    private cd: ChangeDetectorRef,
    public dialog: MatDialog,
    private overlay: Overlay,
    private authStateService: AuthStateService,
  ) {
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
    this.statuses = Object.keys(TaskWizardStatusesEnum)
      .filter(key => isNaN(Number(key))) // Filter out numeric keys that TypeScript adds to enumerations
      .map(key => {
        return {
          label: this.translateService.instant(key),
          value: TaskWizardStatusesEnum[key],
        };
      });
    if (this.model.translates.length === 0) {
      this.model.translates = this.appLanguages.languages
        .filter(x => x.isActive)
        .map(language => ({languageId: language.id, id: undefined, name: '', description: ''}));
    }
    this.repeatEveryArr = generateWeeksList(this.translateService, 52);
    this.repeatTypeArr = Object.keys(RepeatTypeEnum)
      .filter(key => isNaN(Number(key))) // Filter out numeric keys that TypeScript adds to enumerations
      .map(key => {
        return {
          name: this.translateService.instant(key),
          id: RepeatTypeEnum[key],
        };
      });
    this.repeatTypeDay = R.map(x => {
      return {name: x === 1 ? this.translateService.instant('Every') : x.toString(), id: x};
    }, R.range(1, 31)); // 1, 2, ..., 29, 30.
    this.repeatTypeWeek = R.map(x => {
      return {name: x === 1 ? this.translateService.instant('Every') : x.toString(), id: x};
    }, R.range(1, 51)); // 1, 2, ..., 49, 50.
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
    ]; // 1, 2, ..., 23, 24.
    // }, R.range(1, 25)); // 1, 2, ..., 23, 24.
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

  changePropertyId(property: CommonDictionaryModel) {
    this.model.propertyId = property.id;
    this.changeProperty.emit(property.id);
    this.model.folderId = null;
    this.model.sites = [];
    this.selectedFolderName = '';
  }

  changeTagIds(tags: SharedTagModel[]) {
    this.model.tagIds = tags.map(x => x.id);
  }

  changePlanningTagId(tag: SharedTagModel) {
    if(tag) {
      this.model.itemPlanningTagId = tag.id;
    } else {
      this.model.itemPlanningTagId = null;
    }
  }

  /*updateLanguageModel(translationsModel: CommonTranslationsModel, index: number) {
      this.model.translates[index] = translationsModel;
    }*/

  updateName(name: string, index: number) {
    this.model.translates[index].name = name;
  }

  updateEformId(eform: TemplateDto) {
    this.model.eformId = eform.id;
  }

  updateStartDate(e: MatDatepickerInputEvent<any, any>) {
    this.model.startDate = set(e.value, {
      hours: 0,
      minutes: 0,
      seconds: 0,
      milliseconds: 0,
      date: e.value.getDate(),
      year: e.value.getFullYear(),
      month: e.value.getMonth(),
    });
  }

  changeRepeatType(repeatType: { id: number, name: string }) {
    this.model.repeatType = repeatType.id;
  }

  changeRepeatEvery(repeatEvery: { id: number, name: string }) {
    this.model.repeatEvery = repeatEvery.id;
  }

  changeStatus(status: boolean) {
    this.model.status = status ? TaskWizardStatusesEnum.Active : TaskWizardStatusesEnum.NotActive;
  }

  getLanguageName(languageId) {
    return this.appLanguages.languages.find(x => x.id === languageId).name;
  }

  repeatTypeMass() {
    switch (this.model.repeatType) {
      case RepeatTypeEnum.Day: { // day
        return this.repeatTypeDay;
      }
      case RepeatTypeEnum.Week: { // week
        return this.repeatTypeWeek;
      }
      case RepeatTypeEnum.Month: { // month
        return this.repeatTypeMonth;
      }
      default: {
        return [];
      }
    }
  }

  getAssignmentBySiteId(siteId: number): boolean {
    const index = this.model.sites.findIndex(
      (x) => x === siteId
    );
    return index !== -1;
  }

  addToArray(e: MatCheckboxChange, siteId: number) {
    if (e.checked && !this.getAssignmentBySiteId(siteId)) {
      this.model.sites = [...this.model.sites, siteId];
    } else if (!e.checked && this.getAssignmentBySiteId(siteId)) {
      this.model.sites = [...this.model.sites.filter(x => x !== siteId)];
    }
  }

  openFoldersModal() {
    if(this.model.propertyId) {
      const foldersModal = this.dialog.open(TaskWizardFoldersModalComponent,
        {...dialogConfigHelper(this.overlay), hasBackdrop: true});
      foldersModal.backdropClick().pipe(take(1)).subscribe(_ => foldersModal.close());
      foldersModal.componentInstance.folders = this.foldersTreeDto;
      foldersModal.componentInstance.eFormSdkFolderId =
        this.model.folderId ? this.model.folderId : null;
      this.folderSelectedSub$ = foldersModal.componentInstance.folderSelected.subscribe(x => {
        this.model.folderId = x.id;
        this.selectedFolderName = findFullNameById(
          x.id,
          this.foldersTreeDto
        );
      });
    }
  }

  create() {
    this.createTask.emit(this.model);
  }

  openTagsModal() {
    this.planningTagsModal.show();
  }

  hide() {
    this.dialogRef.close();
  }

  ngOnDestroy(): void {
  }
}
