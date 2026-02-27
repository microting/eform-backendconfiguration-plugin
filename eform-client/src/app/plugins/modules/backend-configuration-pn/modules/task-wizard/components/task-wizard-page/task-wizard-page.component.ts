import {
  AfterViewInit, Component, OnDestroy, OnInit, ViewChild,
  inject
} from '@angular/core';
import {AutoUnsubscribe} from 'ngx-auto-unsubscribe';
import {BackendConfigurationPnPropertiesService, BackendConfigurationPnTaskWizardService} from '../../../../services';
import {distinctUntilChanged, filter, tap} from 'rxjs/operators';
import {Subscription, zip} from 'rxjs';
import {CommonDictionaryModel, DeleteModalSettingModel, FolderDto, LanguagesModel} from 'src/app/common/models';
import {ItemsPlanningPnTagsService} from '../../../../../items-planning-pn/services';
import {PlannedTaskWorkers, TaskWizardCreateModel, TaskWizardEditModel, TaskWizardModel} from '../../../../models';
import {TaskWizardStateService} from '../store';
import {DeleteModalComponent} from 'src/app/common/modules/eform-shared/components';
import {dialogConfigHelper, findFullNameById} from 'src/app/common/helpers';
import {TranslateService} from '@ngx-translate/core';
import {MatDialog, MatDialogRef, MatDialogState} from '@angular/material/dialog';
import {Overlay} from '@angular/cdk/overlay';
import {AppSettingsStateService} from 'src/app/modules/application-settings/components/store';
import {
  TaskWizardCreateModalComponent,
  TaskWizardMultipleDeactivateComponent,
  TaskWizardUpdateModalComponent
} from '../../components';
import {PlanningTagsComponent} from '../../../../../items-planning-pn/modules/plannings/components';
import {AuthStateService} from 'src/app/common/store';
import {ActivatedRoute} from '@angular/router';
import {StatisticsStateService} from '../../../statistics/store';
import * as R from 'ramda';
import {selectAuthIsAdmin, selectAuthIsAuth} from 'src/app/state';
import {Store} from '@ngrx/store';
import {
  selectTaskManagementFilters, selectTaskWizardFilters,
  selectTaskWizardPropertyIds
} from '../../../../state';
import {RepeatTypeEnum, TaskWizardStatusesEnum} from "src/app/plugins/modules/backend-configuration-pn/enums";

@AutoUnsubscribe()
@Component({
  selector: 'app-task-wizard-page',
  templateUrl: './task-wizard-page.component.html',
  styleUrls: ['./task-wizard-page.component.scss'],
  standalone: false
})
export class TaskWizardPageComponent implements OnInit, OnDestroy, AfterViewInit {
  private store = inject(Store);
  private propertyService = inject(BackendConfigurationPnPropertiesService);
  private itemsPlanningPnTagsService = inject(ItemsPlanningPnTagsService);
  private taskWizardStateService = inject(TaskWizardStateService);
  private translateService = inject(TranslateService);
  public dialog = inject(MatDialog);
  private overlay = inject(Overlay);
  private backendConfigurationPnTaskWizardService = inject(BackendConfigurationPnTaskWizardService);
  private appSettingsStateService = inject(AppSettingsStateService);
  public authStateService = inject(AuthStateService);
  private route = inject(ActivatedRoute);
  private statisticsStateService = inject(StatisticsStateService);

  @ViewChild('planningTagsModal') planningTagsModal: PlanningTagsComponent;
  properties: CommonDictionaryModel[] = [];
  folders: FolderDto[] = [];
  sites: CommonDictionaryModel[] = [];
  tags: CommonDictionaryModel[] = [];
  tasks: TaskWizardModel[] = [];
  appLanguages: LanguagesModel = new LanguagesModel();
  createModal: MatDialogRef<TaskWizardCreateModalComponent>;
  updateModal: MatDialogRef<TaskWizardUpdateModalComponent>;
  plannedTaskWorkers: PlannedTaskWorkers;
  selectedPropertyId: number | null = null;
  view = [1000, 300];
  showDiagram: boolean = false;

  getPropertiesSub$: Subscription;
  getFoldersSub$: Subscription;
  getSitesSub$: Subscription;
  getPlanningsTagsSub$: Subscription;
  translatesSub$: Subscription;
  taskWizardDeletedSub$: Subscription;
  getLanguagesSub$: Subscription;
  getTaskByIdSub$: Subscription;
  createTaskInModalSub$: Subscription;
  createTaskSub$: Subscription;
  updateTaskSub$: Subscription;
  updateTaskInModalSub$: Subscription;
  tagsChangedSub$: Subscription;
  getFiltersAsyncSub$: Subscription;
  getPropertyIdsAsyncSub$: Subscription;
  changePropertySub$: Subscription;
  getPlannedTaskWorkersSub$: Subscription;
  selectedPlanningsCheckboxes: number[] = [];
  public isAuth$ = this.store.select(selectAuthIsAuth);
  public selectAuthIsAdmin$ = this.store.select(selectAuthIsAdmin);
  deactivateMultipleTasksSub$: Subscription;
  selectedWorkerId: number | null = null;
  selectedStatus: number | null = null;

  get propertyName(): string {
    if (this.properties && this.selectedPropertyId) {
      const index = this.properties.findIndex(x => x.id === this.selectedPropertyId);
      if (index !== -1) {
        return this.properties[index].name;
      }
    }
    return '';
  }

  private selectTaskWizardPropertyIds$ = this.store.select(selectTaskWizardPropertyIds);
  private selectTaskWizardFilters$ = this.store.select(selectTaskWizardFilters);


  constructor() {
    this.route.queryParams.subscribe(x => {
      if (x && x.showDiagram) {
        this.showDiagram = x.showDiagram;
        //this.getPlannedTaskWorkers();
      } else {
        this.showDiagram = false;
      }
    });
  }


  ngOnInit(): void {
    let propertyIds: number[] = [];
    this.getProperties();
    this.getTags();
    this.getTasks();
    this.getEnabledLanguages();
    this.getSites();
    this.getPropertyIdsAsyncSub$ = this.selectTaskWizardPropertyIds$
      .pipe(
        tap(propertyIdList => {
          if (propertyIdList.length !== 0 && !R.equals(propertyIds, propertyIdList)) {
            propertyIds = propertyIdList;
            this.getFolders();
            this.getSites();
          }
        },),
      )
      .subscribe();

    this.getFiltersAsyncSub$ = this.selectTaskWizardFilters$
      .pipe(
        filter(() => this.showDiagram),
        distinctUntilChanged((prev, curr) =>
          R.equals(prev.propertyIds, curr.propertyIds) &&
          R.equals(prev.folderIds, curr.folderIds) &&
          R.equals(prev.tagIds, curr.tagIds) &&
          R.equals(prev.assignToIds, curr.assignToIds) &&
          prev.status === curr.status
        ),

        tap(filters => {
          this.selectedPropertyId = filters.propertyIds?.[0] ?? null;

          this.statisticsStateService
            .getPlannedTaskWorkersByFilters({
              propertyIds: filters.propertyIds ?? [],
              folderIds: filters.folderIds ?? [],
              tagIds: filters.tagIds ?? [],
              assignToIds: filters.assignToIds ?? [],
              status: filters.status ? [filters.status] : [],
            })
            .subscribe(result => {
              if (result?.success) {
                this.plannedTaskWorkers = result.model;
              }
            });
        })
      )
      .subscribe();
  }

  getProperties() {
    this.getPropertiesSub$ = this.backendConfigurationPnTaskWizardService.getAllPropertiesDictionary(false)
      .pipe(tap(data => {
        if (data && data.success && data.model) {
          this.properties = data.model;
        }
      }))
      .subscribe();
  }

  getFolders() {
    let propertyIds: number[] = [];
    this.selectTaskWizardPropertyIds$.subscribe(x => propertyIds = x);
    if (propertyIds.length === 0) {
      return;
    }
    this.getFoldersSub$ = this.propertyService
      .getLinkedFolderListByMultipleProperties(propertyIds)
      .pipe(tap(data => {
        if (data && data.success && data.model) {
          this.folders = data.model;
        }
      }))
      .subscribe();
    // this.getFoldersSub$ = this.propertyService
    //   .getLinkedFolderListByMultipleProperties(this.taskWizardStateService.store.getValue().filters.propertyIds)
    //   .pipe(tap(data => {
    //     if (data && data.success && data.model) {
    //       this.folders = data.model;
    //     }
    //   }))
    //   .subscribe();
  }

  getSites() {
    let propertyIds: number[] = [];
    this.selectTaskWizardPropertyIds$.subscribe(x => propertyIds = x);
    // if (propertyIds.length === 0) {
    //   return;
    // }
    this.getSitesSub$ = this.propertyService
      .getLinkedSitesByMultipleProperties(propertyIds)
      .pipe(tap(result => {
        if (result && result.success && result.success) {
          this.sites = result.model;
        }
      })).subscribe();
  }

  getTags() {
    this.getPlanningsTagsSub$ = this.itemsPlanningPnTagsService.getPlanningsTags()
      .pipe(
        filter(result => !!(result && result.success && result.success)),
        tap(result => {
          this.tags = result.model;
        }),
        tap(() => {
          if (this.createModal && this.createModal.getState() === MatDialogState.OPEN) {
            this.createModal.componentInstance.tags = this.tags;
          }
          if (this.updateModal && this.updateModal.getState() === MatDialogState.OPEN) {
            this.updateModal.componentInstance.tags = this.tags;
          }
        })
      ).subscribe();
  }

  getTasks() {
    this.taskWizardStateService.getAllTasks()
      .pipe(
        tap(data => {
          if (data && data.success && data.model) {
            this.tasks = data.model;
          }
        })
      )
      .subscribe();
  }

  updateTable() {
    this.getTasks();
  }

  onEditTask(model: TaskWizardModel) {
    this.getTaskByIdSub$ = this.backendConfigurationPnTaskWizardService.getTaskById(model.id, false).pipe(
      tap(data => {
        if (data && data.success && data.model) {
          this.updateModal = this.dialog.open(TaskWizardUpdateModalComponent, {
            ...dialogConfigHelper(this.overlay),
            minWidth: 1024
          });
          this.updateModal.componentInstance.fillModelAndCopyModel({
            eformId: data.model.eformId,
            folderId: data.model.folderId,
            propertyId: data.model.propertyId,
            repeatEvery: data.model.repeatEvery,
            repeatType: data.model.repeatEvery === 0 ? 0 : data.model.repeatType,
            sites: data.model.assignedTo,
            startDate: data.model.startDate,
            status: data.model.status,
            tagIds: data.model.tags,
            translates: data.model.translations,
            itemPlanningTagId: data.model.itemPlanningTagId,
            complianceEnabled: data.model.complianceEnabled,
          });
          this.updateModal.componentInstance.typeahead.emit(model.eform);
          this.updateModal.componentInstance.planningTagsModal = this.planningTagsModal;
          this.updateModal.componentInstance.properties = this.properties;
          this.updateModal.componentInstance.tags = this.tags;
          this.updateModal.componentInstance.appLanguages = this.appLanguages;
          if (this.changePropertySub$) {
            this.changePropertySub$.unsubscribe();
          }
          this.changePropertySub$ = this.updateModal.componentInstance.changeProperty.subscribe(propertyId => {
            zip(this.propertyService.getLinkedFolderDtos(propertyId), this.propertyService.getLinkedSites(propertyId, false))
              .subscribe(([folders, sites]) => {
                if (folders && folders.success && folders.model) {
                  this.updateModal.componentInstance.foldersTreeDto = folders.model;
                }
                if (sites && sites.success && sites.model) {
                  this.updateModal.componentInstance.sites = sites.model;
                }
                this.updateModal.componentInstance.selectedFolderName = findFullNameById(
                  this.updateModal.componentInstance.model.folderId,
                  folders.model
                );
              });
          });
          this.updateModal.componentInstance.changeProperty.emit(data.model.propertyId);
          if (this.updateTaskInModalSub$) {
            this.updateTaskInModalSub$.unsubscribe();
          }
          this.updateTaskInModalSub$ = this.updateModal.componentInstance.updateTask.subscribe(updateModel => {
            if (updateModel.repeatType === 0) {
              updateModel.repeatType = 1;
              updateModel.repeatEvery = 0;
            }
            this.updateTask({
              id: model.id,
              eformId: updateModel.eformId,
              folderId: updateModel.folderId,
              propertyId: updateModel.propertyId,
              repeatEvery: updateModel.repeatEvery,
              repeatType: updateModel.repeatType,
              sites: updateModel.sites,
              startDate: updateModel.startDate,
              status: updateModel.status,
              tagIds: updateModel.tagIds,
              translates: updateModel.translates,
              itemPlanningTagId: updateModel.itemPlanningTagId,
              complianceEnabled: updateModel.complianceEnabled,
            }, this.updateModal);
          });
        }
      })
    ).subscribe();
  }

  onCopyTask(model: TaskWizardModel) {
    this.getTaskByIdSub$ = this.backendConfigurationPnTaskWizardService.getTaskById(model.id, false).pipe(
      tap(data => {
        if (data && data.success && data.model) {
          this.createModal = this.dialog.open(TaskWizardCreateModalComponent, {
            ...dialogConfigHelper(this.overlay),
            minWidth: 1024
          });
          if (data.model.repeatType === 1 && data.model.repeatEvery === 0) {
            data.model.repeatType = 0;
            data.model.repeatEvery = 0;
          }
          this.createModal.componentInstance.model = {
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
            complianceEnabled: data.model.complianceEnabled,
          };
          this.createModal.componentInstance.typeahead.emit(model.eform);
          this.createModal.componentInstance.planningTagsModal = this.planningTagsModal;
          this.createModal.componentInstance.properties = this.properties;
          this.createModal.componentInstance.tags = this.tags;
          this.createModal.componentInstance.appLanguages = this.appLanguages;
          if (this.changePropertySub$) {
            this.changePropertySub$.unsubscribe();
          }
          this.changePropertySub$ = this.createModal.componentInstance.changeProperty.subscribe(propertyId => {
            zip(this.propertyService.getLinkedFolderDtos(propertyId), this.propertyService.getLinkedSites(propertyId, false))
              .subscribe(([folders, sites]) => {
                if (folders && folders.success && folders.model) {
                  this.createModal.componentInstance.foldersTreeDto = folders.model;
                }
                if (sites && sites.success && sites.model) {
                  this.createModal.componentInstance.sites = sites.model;
                }
                this.createModal.componentInstance.selectedFolderName = findFullNameById(
                  this.createModal.componentInstance.model.folderId,
                  folders.model
                );
              });
          });
          this.createModal.componentInstance.changeProperty.emit(data.model.propertyId);
          if (this.createTaskInModalSub$) {
            this.createTaskInModalSub$.unsubscribe();
          }
          this.createTaskInModalSub$ = this.createModal.componentInstance.createTask.subscribe(createModel => {
            if (createModel.repeatType === 0) {
              createModel.repeatType = 1;
              createModel.repeatEvery = 0;
            }
            this.createTask(createModel, this.createModal);
          });
        }
      })
    ).subscribe();
  }

  onCreateTask() {
    this.createModal =
      this.dialog.open(TaskWizardCreateModalComponent, {...dialogConfigHelper(this.overlay), minWidth: 1024});
    this.createModal.componentInstance.planningTagsModal = this.planningTagsModal;
    this.createModal.componentInstance.properties = this.properties;
    this.createModal.componentInstance.tags = this.tags;
    this.createModal.componentInstance.appLanguages = this.appLanguages;
    if (this.changePropertySub$) {
      this.changePropertySub$.unsubscribe();
    }
    this.changePropertySub$ = this.createModal.componentInstance.changeProperty.subscribe(propertyId => {
      zip(this.propertyService.getLinkedFolderDtos(propertyId), this.propertyService.getLinkedSites(propertyId, false))
        .subscribe(([folders, sites]) => {
          if (folders && folders.success && folders.model) {
            this.createModal.componentInstance.foldersTreeDto = folders.model;
          }
          if (sites && sites.success && sites.model) {
            this.createModal.componentInstance.sites = sites.model;
          }
          this.createModal.componentInstance.selectedFolderName = findFullNameById(
            this.createModal.componentInstance.model.folderId,
            folders.model
          );
        });
    });
    if (this.createTaskInModalSub$) {
      this.createTaskInModalSub$.unsubscribe();
    }
    this.createTaskInModalSub$ = this.createModal.componentInstance.createTask.subscribe(createModel => {
      if (createModel.repeatType === 0) {
        createModel.repeatType = 1;
        createModel.repeatEvery = 0;
      }
      this.createTask(createModel, this.createModal);
    });
  }

  onDeleteTask(model: TaskWizardModel) {
    this.translatesSub$ = zip(
      this.translateService.stream('Are you sure you want to delete'),
      this.translateService.stream('Name'),
    ).subscribe(([headerText, name]) => {
      const settings: DeleteModalSettingModel = {
        model: model,
        settings: {
          headerText: `${headerText}?`,
          fields: [
            {header: 'ID', field: 'id'},
            {header: name, field: 'taskName'},
          ],
          cancelButtonId: 'taskWizardDeleteCancelBtn',
          deleteButtonId: 'taskWizardDeleteDeleteBtn',
        }
      };
      const deleteTaskWizardModal = this.dialog.open(DeleteModalComponent, {...dialogConfigHelper(this.overlay, settings)});
      this.taskWizardDeletedSub$ = deleteTaskWizardModal.componentInstance.delete
        .subscribe((model: TaskWizardModel) => {
          this.backendConfigurationPnTaskWizardService
            .deleteTaskById(model.id)
            .subscribe((data) => {
              if (data && data.success) {
                deleteTaskWizardModal.close();
                this.updateTable();
              }
            });
        });
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

  createTask(createModel: TaskWizardCreateModel, createModal: MatDialogRef<TaskWizardCreateModalComponent>) {
    this.createTaskSub$ = this.backendConfigurationPnTaskWizardService.createTask(createModel)
      .subscribe((resultCreate) => {
        if (resultCreate && resultCreate.success) {
          createModal.close();
          this.updateTable();
        }
      });
  }

  private updateTask(updateModel: TaskWizardEditModel, updateModal: MatDialogRef<TaskWizardUpdateModalComponent>) {
    this.updateTaskSub$ = this.backendConfigurationPnTaskWizardService.updateTask(updateModel)
      .subscribe((resultCreate) => {
        if (resultCreate && resultCreate.success) {
          updateModal.close();
          this.updateTable();
        }
      });
  }

  openTagsModal() {
    this.planningTagsModal.show();
  }

  ngAfterViewInit() {
    this.tagsChangedSub$ = this.planningTagsModal.tagsChanged.subscribe(() => this.onUpdateTags());
  }

  onUpdateTags() {
    this.getTags();
    this.updateTable();
  }

  getPlannedTaskWorkers() {
    this.getPlannedTaskWorkersSub$ = this.statisticsStateService.getPlannedTaskWorkers(this.selectedPropertyId, null, this.selectedWorkerId)
      .pipe(tap(model => {
        if (model && model.success && model.model) {
          this.plannedTaskWorkers = model.model;
        }
      }))
      .subscribe();
  }

  ngOnDestroy(): void {
  }

  selectedPlanningsChanged(model: number[]) {
    this.selectedPlanningsCheckboxes = model;
  }

  showDeactivateMultipleTasksModal() {
    const taskWizardMultipleDeactivateModal = this.dialog.open(TaskWizardMultipleDeactivateComponent,
      dialogConfigHelper(this.overlay, this.selectedPlanningsCheckboxes.length));
    this.deactivateMultipleTasksSub$ = taskWizardMultipleDeactivateModal.componentInstance.deactivateMultipleTasks
      .subscribe(_ => this.deactivateMultipleTasks(taskWizardMultipleDeactivateModal));
  }

  deactivateMultipleTasks(taskWizardMultipleDeactivateModal: MatDialogRef<TaskWizardMultipleDeactivateComponent>) {
    this.backendConfigurationPnTaskWizardService.deactivateMultipleTasks(this.selectedPlanningsCheckboxes)
      .subscribe((data) => {
        if (data && data.success) {
          taskWizardMultipleDeactivateModal.close();
          this.updateTable();
          this.selectedPlanningsCheckboxes = [];
        }
      });
  }

  private formatDateToDDMMYYYY(date: Date | string): string {
    const d = new Date(date);

    const day = ('0' + d.getDate()).slice(-2);
    const month = ('0' + (d.getMonth() + 1)).slice(-2);
    const year = d.getFullYear();

    return `${day}-${month}-${year}`;
  }

  private escapeCsvValue(value: any): string {
    if (value === null || value === undefined) {
      return '';
    }

    const stringValue = String(value);

    if (stringValue.includes(';') || stringValue.includes('"') || stringValue.includes('\n')) {
      return `"${stringValue.replace(/"/g, '""')}"`;
    }

    return stringValue;
  }

  exportCsv(): void {
    if (!this.tasks || this.tasks.length === 0) {
      return;
    }

    const headers = [
      'Id',
      'Property',
      'Folder',
      'Tags',
      'Report Tag',
      'Task name',
      'eForm',
      'Start date',
      'Repeat',
      'Status',
      'Assigned to'
    ];

    const rows = this.tasks.map(task => {

      const formattedDate = this.formatDateToDDMMYYYY(task.startDate);

      const repeat =
        task.repeatEvery === 0
          ? RepeatTypeEnum[task.repeatType]
          : `${task.repeatEvery} ${RepeatTypeEnum[task.repeatType]}`;

      return [
        task.id,
        task.property ?? '',
        task.folder ?? '',
        task.tags?.map(t => t.name).join(', ') ?? '',
        task.tagReport?.name ?? '',
        task.taskName ?? '',
        task.eform ?? '',
        formattedDate,
        repeat,
        TaskWizardStatusesEnum[task.status],
        task.assignedTo?.join(', ') ?? ''
      ];
    });

    const csvContent =
      headers.join(';') +
      '\n' +
      rows.map(r => r.map(v => this.escapeCsvValue(v)).join(';')).join('\n');

    const blob = new Blob([csvContent], {
      type: 'text/csv;charset=utf-8;'
    });

    const fileName = `tasks_${this.formatDateToDDMMYYYY(new Date())}.csv`;

    const link = document.createElement('a');
    link.href = URL.createObjectURL(blob);
    link.download = fileName;
    link.click();

    URL.revokeObjectURL(link.href);
  }
}
