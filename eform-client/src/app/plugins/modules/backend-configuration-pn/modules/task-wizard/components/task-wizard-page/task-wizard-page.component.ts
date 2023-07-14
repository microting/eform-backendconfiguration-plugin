import {AfterViewInit, Component, OnDestroy, OnInit, ViewChild,} from '@angular/core';
import {AutoUnsubscribe} from 'ngx-auto-unsubscribe';
import {BackendConfigurationPnPropertiesService, BackendConfigurationPnTaskWizardService} from '../../../../services';
import {filter, tap} from 'rxjs/operators';
import {Subscription, zip} from 'rxjs';
import {CommonDictionaryModel, DeleteModalSettingModel, FolderDto, LanguagesModel} from 'src/app/common/models';
import {FoldersService, SitesService} from 'src/app/common/services';
import {ItemsPlanningPnTagsService} from '../../../../../items-planning-pn/services';
import {TaskWizardCreateModel, TaskWizardEditModel, TaskWizardModel} from '../../../../models';
import {TaskWizardStateService} from '../store';
import {DeleteModalComponent} from 'src/app/common/modules/eform-shared/components';
import {dialogConfigHelper, findFullNameById} from 'src/app/common/helpers';
import {TranslateService} from '@ngx-translate/core';
import {MatDialog, MatDialogRef, MatDialogState} from '@angular/material/dialog';
import {Overlay} from '@angular/cdk/overlay';
import {AppSettingsStateService} from 'src/app/modules/application-settings/components/store';
import {TaskWizardCreateModalComponent, TaskWizardUpdateModalComponent} from '../../components';
import {PlanningTagsComponent} from '../../../../../items-planning-pn/modules/plannings/components';
import {AuthStateService} from 'src/app/common/store';

@AutoUnsubscribe()
@Component({
  selector: 'app-task-wizard-page',
  templateUrl: './task-wizard-page.component.html',
  styleUrls: ['./task-wizard-page.component.scss'],
})
export class TaskWizardPageComponent implements OnInit, OnDestroy, AfterViewInit {
  @ViewChild('planningTagsModal') planningTagsModal: PlanningTagsComponent;
  properties: CommonDictionaryModel[] = [];
  folders: FolderDto[] = [];
  sites: CommonDictionaryModel[] = [];
  tags: CommonDictionaryModel[] = [];
  tasks: TaskWizardModel[] = [];
  appLanguages: LanguagesModel = new LanguagesModel();
  foldersTreeDto: FolderDto[];
  createModal: MatDialogRef<TaskWizardCreateModalComponent, any>;
  updateModal: MatDialogRef<TaskWizardUpdateModalComponent, any>;

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

  constructor(
    private propertyService: BackendConfigurationPnPropertiesService,
    private folderService: FoldersService,
    private sitesService: SitesService,
    private itemsPlanningPnTagsService: ItemsPlanningPnTagsService,
    private taskWizardStateService: TaskWizardStateService,
    private translateService: TranslateService,
    public dialog: MatDialog,
    private overlay: Overlay,
    private backendConfigurationPnTaskWizardService: BackendConfigurationPnTaskWizardService,
    private appSettingsStateService: AppSettingsStateService,
    private authStateService: AuthStateService,
  ) {
  }

  ngOnInit(): void {
    this.getProperties();
    this.getFolders();
    this.getFoldersTree();
    this.getSites();
    this.getTags();
    this.getTasks();
    this.getEnabledLanguages();
  }

  getProperties() {
    this.getPropertiesSub$ = this.backendConfigurationPnTaskWizardService.getAllPropertiesDictionary()
      .pipe(tap(data => {
        if (data && data.success && data.model) {
          this.properties = data.model;
        }
      }))
      .subscribe();
  }

  getFolders() {
    this.getFoldersSub$ = this.folderService.getAllFoldersList()
      .pipe(tap(data => {
        if (data && data.success && data.model) {
          this.folders = data.model;
        }
      }))
      .subscribe();
  }

  getSites() {
    this.getSitesSub$ = this.sitesService.getAllSitesDictionary()
      .pipe(tap(result => {
        if (result && result.success && result.success) {
          this.sites = result.model;
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
          if(this.createModal && this.createModal.getState() === MatDialogState.OPEN) {
            this.createModal.componentInstance.tags = this.tags;
          }
          if(this.updateModal && this.updateModal.getState() === MatDialogState.OPEN) {
            this.updateModal.componentInstance.tags = this.tags;
          }
        })
        )
      .subscribe();
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

  getFoldersTree() {
    this.folderService.getAllFolders()
      .pipe(
        tap((operation) => {
          if (operation && operation.success) {
            this.foldersTreeDto = operation.model;
          }
        })
      )
      .subscribe();
  }

  updateTable() {
    this.getTasks();
  }

  onEditTask(model: TaskWizardModel) {
    this.getTaskByIdSub$ = this.backendConfigurationPnTaskWizardService.getTaskById(model.id).pipe(
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
          });
          this.updateModal.componentInstance.typeahead.emit(model.eform);
          this.updateModal.componentInstance.planningTagsModal = this.planningTagsModal;
          this.updateModal.componentInstance.folders = this.folders;
          this.updateModal.componentInstance.properties = this.properties;
          this.updateModal.componentInstance.sites = this.sites;
          this.updateModal.componentInstance.tags = this.tags;
          this.updateModal.componentInstance.appLanguages = this.appLanguages;
          this.updateModal.componentInstance.foldersTreeDto = this.foldersTreeDto;
          this.updateModal.componentInstance.selectedFolderName  = findFullNameById(
            data.model.folderId,
            this.foldersTreeDto
          );
          if (this.updateTaskInModalSub$) {
            this.updateTaskInModalSub$.unsubscribe();
          }
          this.updateTaskInModalSub$ = this.updateModal.componentInstance.updateTask.subscribe(updateModel => {
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
              translates: data.model.translations,
            }, this.updateModal);
          });
        }
      })
    ).subscribe();
  }

  onCopyTask(model: TaskWizardModel) {
    this.getTaskByIdSub$ = this.backendConfigurationPnTaskWizardService.getTaskById(model.id).pipe(
      tap(data => {
        if (data && data.success && data.model) {
          this.createModal = this.dialog.open(TaskWizardCreateModalComponent, {...dialogConfigHelper(this.overlay), minWidth: 600});
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
          };
          this.createModal.componentInstance.typeahead.emit(model.eform);
          this.createModal.componentInstance.planningTagsModal = this.planningTagsModal;
          this.createModal.componentInstance.folders = this.folders;
          this.createModal.componentInstance.properties = this.properties;
          this.createModal.componentInstance.sites = this.sites;
          this.createModal.componentInstance.tags = this.tags;
          this.createModal.componentInstance.appLanguages = this.appLanguages;
          this.createModal.componentInstance.foldersTreeDto = this.foldersTreeDto;
          this.createModal.componentInstance.selectedFolderName  = findFullNameById(
            data.model.folderId,
            this.foldersTreeDto
          );
          if (this.createTaskInModalSub$) {
            this.createTaskInModalSub$.unsubscribe();
          }
          this.createTaskInModalSub$ = this.createModal.componentInstance.createTask.subscribe(createModel => {
            this.createTask(createModel, this.createModal);
          });
        }
      })
    ).subscribe();
  }

  onCreateTask() {
    this.createModal = this.dialog.open(TaskWizardCreateModalComponent, {...dialogConfigHelper(this.overlay), minWidth: 600});
    this.createModal.componentInstance.planningTagsModal = this.planningTagsModal;
    this.createModal.componentInstance.folders = this.folders;
    this.createModal.componentInstance.properties = this.properties;
    this.createModal.componentInstance.sites = this.sites;
    this.createModal.componentInstance.tags = this.tags;
    this.createModal.componentInstance.appLanguages = this.appLanguages;
    this.createModal.componentInstance.foldersTreeDto = this.foldersTreeDto;
    if (this.createTaskInModalSub$) {
      this.createTaskInModalSub$.unsubscribe();
    }
    this.createTaskInModalSub$ = this.createModal.componentInstance.createTask.subscribe(createModel => {
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
    this.planningTagsModal.show(this.authStateService.isAdmin);
  }

  ngAfterViewInit() {
    this.tagsChangedSub$ = this.planningTagsModal.tagsChanged.subscribe(() => this.onUpdateTags());
  }

  onUpdateTags() {
    this.getTags();
    this.updateTable();
  }

  ngOnDestroy(): void {
  }
}
