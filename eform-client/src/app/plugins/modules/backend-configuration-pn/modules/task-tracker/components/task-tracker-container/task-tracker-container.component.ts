import {Component, OnDestroy, OnInit, ViewChild} from '@angular/core';
import {AutoUnsubscribe} from 'ngx-auto-unsubscribe';
import {
  TaskModel,
  Columns,
  TaskWizardEditModel,
  TaskWizardCreateModel,
} from '../../../../models';
import {TaskTrackerStateService} from '../store';
import {
  TaskTrackerCreateShowModalComponent,
  TaskTrackerShownColumnsComponent
} from '../';
import {
  BackendConfigurationPnAreasService,
  BackendConfigurationPnPropertiesService,
  BackendConfigurationPnTaskTrackerService,
  BackendConfigurationPnTaskWizardService
} from '../../../../services';
import {ToastrService} from 'ngx-toastr';
import {Subscription, zip} from 'rxjs';
import {MatDialog, MatDialogRef} from '@angular/material/dialog';
import {Overlay} from '@angular/cdk/overlay';
import {dialogConfigHelper, findFullNameById} from 'src/app/common/helpers';
import {LoaderService} from 'src/app/common/services';
import {MatIconRegistry} from '@angular/material/icon';
import {DomSanitizer} from '@angular/platform-browser';
import {ExcelIcon} from 'src/app/common/const';
import {catchError, tap} from 'rxjs/operators';
import {saveAs} from 'file-saver';
import {format} from 'date-fns';
import {CommonDictionaryModel, LanguagesModel} from 'src/app/common/models';
import {
  TaskWizardCreateModalComponent,
  TaskWizardUpdateModalComponent
} from '../../../../modules/task-wizard/components';
import {AppSettingsStateService} from 'src/app/modules/application-settings/components/store';
import {ItemsPlanningPnTagsService} from 'src/app/plugins/modules/items-planning-pn/services';
import {PlanningTagsComponent} from 'src/app/plugins/modules/items-planning-pn/modules/plannings/components';

@AutoUnsubscribe()
@Component({
  selector: 'app-task-tracker-container',
  templateUrl: './task-tracker-container.component.html',
  styleUrls: ['./task-tracker-container.component.scss'],
})
export class TaskTrackerContainerComponent implements OnInit, OnDestroy {
  @ViewChild('planningTagsModal') planningTagsModal: PlanningTagsComponent;
  tasks: TaskModel[] = [];
  columns: Columns = {
    deadline: true,
    property: true,
    repeat: true,
    start: true,
    tags: true,
    task: true,
    workers: true,
    calendar: true,
  };
  properties: CommonDictionaryModel[] = [];
  tags: CommonDictionaryModel[] = [];
  appLanguages: LanguagesModel = new LanguagesModel();
  createModal: MatDialogRef<TaskWizardCreateModalComponent>;
  updateModal: MatDialogRef<TaskWizardUpdateModalComponent>;

  getAllTasksSub$: Subscription;
  taskCreatedSub$: Subscription;
  columnsChangedSub$: Subscription;
  updateColumnsSub$: Subscription;
  getColumnsSub$: Subscription;
  downloadExcelReportSub$: Subscription;
  getAllPropertiesDictionarySub$: Subscription;
  getPlanningsTagsSub$: Subscription;
  getLanguagesSub$: Subscription;
  changePropertySub$: Subscription;
  updateTaskInModalSub$: Subscription;
  getTaskByIdSub$: Subscription;
  updateTaskSub$: Subscription;
  createTaskSub$: Subscription;
  createTaskInModalSub$: Subscription;

  constructor(
    private loaderService: LoaderService,
    public taskTrackerStateService: TaskTrackerStateService,
    public taskTrackerService: BackendConfigurationPnTaskTrackerService,
    private toasterService: ToastrService,
    private propertyService: BackendConfigurationPnPropertiesService,
    public dialog: MatDialog,
    private overlay: Overlay,
    private areasService: BackendConfigurationPnAreasService,
    iconRegistry: MatIconRegistry,
    sanitizer: DomSanitizer,
    private backendConfigurationPnTaskWizardService: BackendConfigurationPnTaskWizardService,
    private appSettingsStateService: AppSettingsStateService,
    private itemsPlanningPnTagsService: ItemsPlanningPnTagsService,
  ) {
    iconRegistry.addSvgIconLiteral('file-excel', sanitizer.bypassSecurityTrustHtml(ExcelIcon));
  }

  ngOnInit() {
    this.updateTable();
    this.getColumns();
    this.getProperties();
    this.getEnabledLanguages();
    this.getTags();
  }

  ngOnDestroy(): void {
  }

  openColumnsModal(): void {
    const dialogRef = this.dialog.open(TaskTrackerShownColumnsComponent, dialogConfigHelper(this.overlay, this.columns));
    this.getColumns(dialogRef);
    this.columnsChangedSub$ = dialogRef.componentInstance.columnsChanged.subscribe((data: Columns) => {
      let updateModal = [];
      if (data) {
        for (let [key, value] of Object.entries(data)) {
          if (data.hasOwnProperty(key)) {
            updateModal = [...updateModal, {columnName: key, isColumnEnabled: value}];
          }
        }
      }
      this.updateColumnsSub$ = this.taskTrackerService.updateColumns(updateModal).subscribe(response => {
        if (response && response.success) {
          dialogRef.close();
          this.getColumns();
        }
      });
    });
  }

  getColumns(dialogRef?: MatDialogRef<TaskTrackerShownColumnsComponent>) {
    this.getColumnsSub$ = this.taskTrackerService.getColumns().subscribe(data => {
      if (data && data.success && data.model && data.model.length) {
        this.columns = data.model.reduce((acc, {columnName, isColumnEnabled}) => {
          acc[columnName] = isColumnEnabled;
          return acc;
        }, {} as Columns);
        if (dialogRef) {
          dialogRef.componentInstance.setColumns(this.columns);
        }
      }
    });
  }

  updateTable() {
    this.getAllTasksSub$ = this.taskTrackerStateService
      .getAllTasks()
      .subscribe((data) => {
        if (data && data.success && data.model) {
          this.tasks = data.model;
        }
      });
  }

  openCreateModal() {
    const createModal = this.dialog.open(TaskTrackerCreateShowModalComponent, dialogConfigHelper(this.overlay));
    this.taskCreatedSub$ = createModal.componentInstance.taskCreated.subscribe(() => this.updateTable());
  }

  openEditTaskModal(task: TaskModel) {
    this.getTaskByIdSub$ = this.backendConfigurationPnTaskWizardService.getTaskById(task.areaRulePlanId).pipe(
      tap(data => {
        if (data && data.success && data.model) {
          this.updateModal = this.dialog.open(TaskWizardUpdateModalComponent, {...dialogConfigHelper(this.overlay), minWidth: 600});
          this.updateModal.componentInstance.fillModelAndCopyModel({
            eformId: data.model.eformId,
            folderId: data.model.folderId,
            propertyId: data.model.propertyId,
            repeatEvery: data.model.repeatEvery,
            repeatType: data.model.repeatType,
            sites: data.model.assignedTo,
            startDate: data.model.startDate,
            status: data.model.status,
            tagIds: data.model.tags,
            translates: data.model.translations,
            itemPlanningTagId: data.model.itemPlanningTagId,
          });
          this.updateModal.componentInstance.typeahead.emit(data.model.eformName);
          this.updateModal.componentInstance.planningTagsModal = this.planningTagsModal;
          this.updateModal.componentInstance.properties = this.properties;
          this.updateModal.componentInstance.tags = this.tags;
          this.updateModal.componentInstance.appLanguages = this.appLanguages;
          if(this.changePropertySub$) {
            this.changePropertySub$.unsubscribe()
          }
          this.changePropertySub$ = this.updateModal.componentInstance.changeProperty.subscribe(propertyId => {
            zip(this.propertyService.getLinkedFolderDtos(propertyId), this.propertyService.getLinkedSites(propertyId))
              .subscribe(([folders, sites]) => {
                if(folders && folders.success && folders.model) {
                  this.updateModal.componentInstance.foldersTreeDto = folders.model;
                }
                if(sites && sites.success && sites.model) {
                  this.updateModal.componentInstance.sites = sites.model;
                }
                this.updateModal.componentInstance.selectedFolderName  = findFullNameById(
                  this.updateModal.componentInstance.model.folderId,
                  folders.model
                );
              })
          })
          this.updateModal.componentInstance.changeProperty.emit(data.model.propertyId)
          if (this.updateTaskInModalSub$) {
            this.updateTaskInModalSub$.unsubscribe();
          }
          this.updateTaskInModalSub$ = this.updateModal.componentInstance.updateTask.subscribe(updateModel => {
            this.updateTask({
              id: data.model.id,
              eformId: updateModel.eformId,
              folderId: updateModel.folderId,
              propertyId: updateModel.propertyId,
              repeatEvery: updateModel.repeatEvery,
              repeatType: updateModel.repeatType,
              sites: updateModel.sites,
              startDate: updateModel.startDate,
              status: updateModel.status,
              tagIds: updateModel.tagIds,
              translates: data.model.translations,
              itemPlanningTagId: data.model.itemPlanningTagId,
            }, this.updateModal);
          });
        }
      })
    ).subscribe();
  }

  openCreateTaskModal() {
    this.createModal = this.dialog.open(TaskWizardCreateModalComponent, {...dialogConfigHelper(this.overlay), minWidth: 600});
    this.createModal.componentInstance.planningTagsModal = this.planningTagsModal;
    this.createModal.componentInstance.properties = this.properties;
    this.createModal.componentInstance.tags = this.tags;
    this.createModal.componentInstance.appLanguages = this.appLanguages;
    if(this.changePropertySub$) {
      this.changePropertySub$.unsubscribe()
    }
    this.changePropertySub$ = this.createModal.componentInstance.changeProperty.subscribe(propertyId => {
      zip(this.propertyService.getLinkedFolderDtos(propertyId), this.propertyService.getLinkedSites(propertyId))
        .subscribe(([folders, sites]) => {
          if(folders && folders.success && folders.model) {
            this.createModal.componentInstance.foldersTreeDto = folders.model;
          }
          if(sites && sites.success && sites.model) {
            this.createModal.componentInstance.sites = sites.model;
          }
        })
    })
    if (this.createTaskInModalSub$) {
      this.createTaskInModalSub$.unsubscribe();
    }
    this.createTaskInModalSub$ = this.createModal.componentInstance.createTask.subscribe(createModel => {
      this.createTask(createModel, this.createModal);
    });
  }

  updateTask(updateModel: TaskWizardEditModel, updateModal: MatDialogRef<TaskWizardUpdateModalComponent>) {
    this.updateTaskSub$ = this.backendConfigurationPnTaskWizardService.updateTask(updateModel)
      .subscribe((resultCreate) => {
        if (resultCreate && resultCreate.success) {
          updateModal.close();
          this.updateTable();
        }
      });
  }

  createTask(createModel: TaskWizardCreateModel, createModal: MatDialogRef<TaskWizardCreateModalComponent>) {
    this.createTaskSub$ = this.backendConfigurationPnTaskWizardService.createTask(createModel)
      .subscribe((resultCreate) => {
        if (resultCreate && resultCreate.success) {
          createModal.close();
          this.updateTable();
        }
      });
  }

  onDownloadExcelReport() {
    const filters = this.taskTrackerStateService.store.getValue().filters;
    this.downloadExcelReportSub$ = this.taskTrackerService
      .downloadExcelReport(filters)
      .pipe(
        tap((data) => {
          saveAs(data, `TT_${format(new Date(), 'yyyy/MM/dd')}_report.xlsx`);
        }),
        catchError((_, caught) => {
          this.toasterService.error('Error downloading report');
          return caught;
        }),
      )
      .subscribe();
  }

  getProperties() {
    this.getAllPropertiesDictionarySub$ = this.propertyService
      .getAllPropertiesDictionary(true)
      .subscribe((data) => {
        if (data && data.success && data.model) {
          this.properties = [...data.model];
        }
      });
  }

  getTags() {
    this.getPlanningsTagsSub$ = this.itemsPlanningPnTagsService.getPlanningsTags().subscribe((result) => {
      if (result && result.success && result.success) {
        this.tags = [...result.model];
      }
    });
  }

  getEnabledLanguages() {
    this.getLanguagesSub$ = this.appSettingsStateService.getLanguages()
      .pipe(tap(data => {
        if (data && data.success && data.model) {
          this.appLanguages = data.model;
        }
      }))
      .subscribe();
  }
}
