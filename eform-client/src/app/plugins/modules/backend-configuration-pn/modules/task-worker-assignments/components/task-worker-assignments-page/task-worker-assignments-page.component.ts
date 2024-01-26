import {Component, EventEmitter, OnDestroy, OnInit, Output, ViewChild} from '@angular/core';
import {AutoUnsubscribe} from 'ngx-auto-unsubscribe';
import {
  CommonDictionaryModel, LanguagesModel,
  Paged,
} from 'src/app/common/models';
import {TaskWorkerAssignmentsStateService} from '../store';
import {AreaRulePlanningModel, TaskWizardEditModel, TaskWizardModel, TaskWorkerModel} from '../../../../models';
import {ActivatedRoute} from '@angular/router';
import {SitesService} from 'src/app/common/services';
import {AreaRulePlanModalComponent} from '../../../../components';
import {
  BackendConfigurationPnAreasService,
  BackendConfigurationPnPropertiesService,
  BackendConfigurationPnTaskWizardService
} from '../../../../services';
import {Subscription, zip} from 'rxjs';
import {Sort} from '@angular/material/sort';
import {MtxGridColumn} from '@ng-matero/extensions/grid';
import {TranslateService} from '@ngx-translate/core';
import {MatDialog, MatDialogRef, MatDialogState} from '@angular/material/dialog';
import {Overlay} from '@angular/cdk/overlay';
import {dialogConfigHelper, findFullNameById} from 'src/app/common/helpers';
import {Store} from '@ngrx/store';
import {
  selectTaskWorkerAssignmentPaginationIsSortDsc,
  selectTaskWorkerAssignmentPaginationSort
} from 'src/app/plugins/modules/backend-configuration-pn/state/task-worker-assignment/task-worker-assignment.selector';
import {filter, tap} from 'rxjs/operators';
import {
  TaskWizardCreateModalComponent,
  TaskWizardUpdateModalComponent
} from 'src/app/plugins/modules/backend-configuration-pn/modules/task-wizard/components';
import {PlanningTagsComponent} from 'src/app/plugins/modules/items-planning-pn/modules/plannings/components';
import {ItemsPlanningPnTagsService} from 'src/app/plugins/modules/items-planning-pn/services';
import {AppSettingsStateService} from 'src/app/modules/application-settings/components/store';

@AutoUnsubscribe()
@Component({
  selector: 'app-property-worker-assignments-page',
  templateUrl: './task-worker-assignments-page.component.html',
})
export class TaskWorkerAssignmentsPageComponent implements OnInit, OnDestroy {
  @ViewChild('planningTagsModal') planningTagsModal: PlanningTagsComponent;
  tableHeaders: MtxGridColumn[] = [
    {
      header: this.translateService.stream('ID'),
      field: 'id',
      sortProp: {id: 'Id'},
      sortable: true,
    },
    {
      header: this.translateService.stream('Property name'),
      field: 'propertyName',
      sortProp: {id: 'PropertyName'},
      sortable: true,
    },
    {
      field: 'path',
      header: this.translateService.stream('Control area'),
      sortProp: {id: 'Path'},
      sortable: true,
    },
    {
      header: this.translateService.stream('Checkpoint'),
      field: 'itemName',
      sortProp: {id: 'ItemName'},
      sortable: true,
    },
    {
      header: this.translateService.stream('Actions'),
      field: 'actions',
      type: 'button',
      buttons: [
        {
          tooltip: this.translateService.stream('Show planning'),
          type: 'icon',
          color: 'accent',
          icon: 'visibility',
          click: (rowData: TaskWorkerModel) => this.onEditTask(rowData),
          //click: (rowData: TaskWorkerModel) => this.openAreaRulePlanModal(rowData),
        },
      ],
    },
  ];

  taskWorkerAssignments: Paged<TaskWorkerModel> = new Paged<TaskWorkerModel>();
  properties: CommonDictionaryModel[] = [];
  tags: CommonDictionaryModel[] = [];
  appLanguages: LanguagesModel = new LanguagesModel();
  siteName: string;
  createModal: MatDialogRef<TaskWizardCreateModalComponent>;

  planAreaRuleSub$: Subscription;
  getSingleSiteSub$: Subscription;
  getAreaByRuleIdSub$: Subscription;
  updateAreaRulePlanSub$: Subscription;
  updateModal: MatDialogRef<TaskWizardUpdateModalComponent>;
  getTaskByIdSub$: Subscription;
  changePropertySub$: Subscription;
  updateTaskInModalSub$: Subscription;
  getAreaRulePlanningByPlanningIdSub$: Subscription;
  updateTaskSub$: Subscription;
  getPropertiesSub$: Subscription;
  getLanguagesSub$: Subscription;
  getPlanningsTagsSub$: Subscription;
  public selectTaskWorkerAssignmentPaginationSort$ = this.store.select(selectTaskWorkerAssignmentPaginationSort);
  public selectTaskWorkerAssignmentPaginationIsSortDsc$ = this.store.select(selectTaskWorkerAssignmentPaginationIsSortDsc);

  constructor(
    private store: Store,
    public taskWorkerAssignmentsStateService: TaskWorkerAssignmentsStateService,
    private activatedRoute: ActivatedRoute,
    private sitesService: SitesService,
    private backendConfigurationPnAreasService: BackendConfigurationPnAreasService,
    private areasService: BackendConfigurationPnAreasService,
    private translateService: TranslateService,
    private dialog: MatDialog,
    private overlay: Overlay,
    private propertyService: BackendConfigurationPnPropertiesService,
    private backendConfigurationPnTaskWizardService: BackendConfigurationPnTaskWizardService,
    private itemsPlanningPnTagsService: ItemsPlanningPnTagsService,
    private appSettingsStateService: AppSettingsStateService,
  ) {
  }

  ngOnInit() {
    this.activatedRoute.params.subscribe((params) => {
      this.taskWorkerAssignmentsStateService.siteId = +params['siteId'];
      this.getSiteName();
      this.getTaskWorkerAssignments();
      this.getProperties();
      this.getTags();
      this.getEnabledLanguages();
    });
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

  getEnabledLanguages() {
    this.getLanguagesSub$ = this.appSettingsStateService.getLanguages()
      .pipe(tap(data => {
        if (data && data.success && data.model) {
          this.appLanguages = data.model;
        }
      }))
      .subscribe();
  }


  sortTable(sort: Sort) {
    this.taskWorkerAssignmentsStateService.onSortTable(sort.active);
    this.getTaskWorkerAssignments();
  }

  getTaskWorkerAssignments() {
    this.taskWorkerAssignmentsStateService
      .getTaskWorkerAssignments()
      .subscribe((data) => {
        if (data && data.success && data.model) {
          this.taskWorkerAssignments = data.model;
        }
      })
  }

  getSiteName() {
    this.getSingleSiteSub$ = this.sitesService
      .getSingleSite(this.taskWorkerAssignmentsStateService.siteId)
      .subscribe((data) => {
        if (data && data.success && data.model) {
          this.siteName = data.model.siteName;
        }
      })
  }

  openAreaRulePlanModal(taskWorkerAssignments: TaskWorkerModel) {
    this.getAreaRulePlanningByPlanningIdSub$ = this.backendConfigurationPnAreasService
      .getAreaRulePlanningByPlanningId(taskWorkerAssignments.id)
      .subscribe((areaRulePlan) => {
        if (areaRulePlan && areaRulePlan.success && areaRulePlan.model) {
          this.getAreaByRuleIdSub$ = this.backendConfigurationPnAreasService.getAreaByRuleId(areaRulePlan.model.ruleId)
            .subscribe((area) => {
              if (area && area.success && area.model) {
                const modal = this.dialog.open(AreaRulePlanModalComponent,
                  {
                    ...dialogConfigHelper(this.overlay,
                      {
                        areaRule: taskWorkerAssignments.areaRule,
                        propertyId: taskWorkerAssignments.propertyId,
                        area: area.model,
                        areaRulePlan: areaRulePlan.model,
                      }),
                    minWidth: 500,
                  });
                this.updateAreaRulePlanSub$ = modal.componentInstance.updateAreaRulePlan
                  .subscribe(x => this.onUpdateAreaRulePlan(x, modal))
              }
            });
        }
      });
  }

  onEditTask(model: TaskWorkerModel) {
    this.getTaskByIdSub$ = this.backendConfigurationPnTaskWizardService.getTaskById(model.id).pipe(
      tap(data => {
        if (data && data.success && data.model) {
          this.updateModal = this.dialog.open(TaskWizardUpdateModalComponent, {...dialogConfigHelper(this.overlay), minWidth: 800});
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
          });
          this.updateModal.componentInstance.typeahead.emit(data.model.eformName);
          this.updateModal.componentInstance.planningTagsModal = this.planningTagsModal;
          this.updateModal.componentInstance.properties = this.properties;
          this.updateModal.componentInstance.tags = this.tags;
          this.updateModal.componentInstance.appLanguages = this.appLanguages;
          if (this.changePropertySub$) {
            this.changePropertySub$.unsubscribe();
          }
          this.changePropertySub$ = this.updateModal.componentInstance.changeProperty.subscribe(propertyId => {
            zip(this.propertyService.getLinkedFolderDtos(propertyId), this.propertyService.getLinkedSites(propertyId))
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
            }, this.updateModal);
          });
        }
      })
    ).subscribe();
  }

  onUpdateAreaRulePlan(rulePlanning: AreaRulePlanningModel, modal: MatDialogRef<AreaRulePlanModalComponent>) {
    this.planAreaRuleSub$ = this.areasService
      .updateAreaRulePlanning(rulePlanning)
      .subscribe((data) => {
        if (data && data.success) {
          modal.close();
        }
      });
  }

  ngOnDestroy(): void {
  }

  private updateTask(updateModel: TaskWizardEditModel, updateModal: MatDialogRef<TaskWizardUpdateModalComponent>) {
    this.updateTaskSub$ = this.backendConfigurationPnTaskWizardService.updateTask(updateModel)
      .subscribe((resultCreate) => {
        if (resultCreate && resultCreate.success) {
          updateModal.close();
          //this.updateTable();
        }
      });
  }
}
