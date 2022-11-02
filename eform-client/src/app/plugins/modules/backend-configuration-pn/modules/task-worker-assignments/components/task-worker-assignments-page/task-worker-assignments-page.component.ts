import {Component, OnDestroy, OnInit} from '@angular/core';
import {AutoUnsubscribe} from 'ngx-auto-unsubscribe';
import {
  Paged,
} from 'src/app/common/models';
import {TaskWorkerAssignmentsStateService} from '../store';
import {AreaRulePlanningModel, TaskWorkerModel} from '../../../../models';
import {ActivatedRoute} from '@angular/router';
import {SitesService} from 'src/app/common/services';
import {AreaRulePlanModalComponent} from '../../../../components';
import {BackendConfigurationPnAreasService} from '../../../../services';
import {Subscription} from 'rxjs';
import {Sort} from '@angular/material/sort';
import {MtxGridColumn} from '@ng-matero/extensions/grid';
import {TranslateService} from '@ngx-translate/core';
import {MatDialog, MatDialogRef} from '@angular/material/dialog';
import {Overlay} from '@angular/cdk/overlay';
import {dialogConfigHelper} from 'src/app/common/helpers';

@AutoUnsubscribe()
@Component({
  selector: 'app-property-worker-assignments-page',
  templateUrl: './task-worker-assignments-page.component.html',
})
export class TaskWorkerAssignmentsPageComponent implements OnInit, OnDestroy {
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
          click: (rowData: TaskWorkerModel) => this.openAreaRulePlanModal(rowData),
        },
      ],
    },
  ];

  taskWorkerAssignments: Paged<TaskWorkerModel> = new Paged<TaskWorkerModel>();
  siteName: string;

  planAreaRuleSub$: Subscription;
  getSingleSiteSub$: Subscription;
  getAreaByRuleIdSub$: Subscription;
  updateAreaRulePlanSub$: Subscription;
  getAreaRulePlanningByPlanningIdSub$: Subscription;

  constructor(
    public taskWorkerAssignmentsStateService: TaskWorkerAssignmentsStateService,
    private activatedRoute: ActivatedRoute,
    private sitesService: SitesService,
    private backendConfigurationPnAreasService: BackendConfigurationPnAreasService,
    private areasService: BackendConfigurationPnAreasService,
    private translateService: TranslateService,
    private dialog: MatDialog,
    private overlay: Overlay,
  ) {
  }

  ngOnInit() {
    this.activatedRoute.params.subscribe((params) => {
      this.taskWorkerAssignmentsStateService.siteId = +params['siteId'];
      this.getSiteName();
      this.getTaskWorkerAssignments();
    });
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
}
