import {Component, OnDestroy, OnInit, ViewChild} from '@angular/core';
import {AutoUnsubscribe} from 'ngx-auto-unsubscribe';
import {
  Paged,
  TableHeaderElementModel,
} from 'src/app/common/models';
import {TaskWorkerAssignmentsStateService} from '../store';
import {AreaRulePlanningModel, TaskWorkerModel} from '../../../../models';
import {ActivatedRoute} from '@angular/router';
import {SitesService} from 'src/app/common/services';
import {AreaRulePlanModalComponent} from '../../../../components';
import {BackendConfigurationPnAreasService} from 'src/app/plugins/modules/backend-configuration-pn/services';
import {Subscription} from 'rxjs';
import {area} from 'd3';

@AutoUnsubscribe()
@Component({
  selector: 'app-property-worker-assignments-page',
  templateUrl: './task-worker-assignments-page.component.html',
})
export class TaskWorkerAssignmentsPageComponent implements OnInit, OnDestroy {
  @ViewChild('areaRulePlanModal') areaRulePlanModal: AreaRulePlanModalComponent;
  tableHeaders: TableHeaderElementModel[] = [
    {
      name: 'Id',
      visibleName: 'ID',
      sortable: true,
    },
    {name: 'PropertyName', visibleName: 'Property', sortable: true},
    {name: 'Path', sortable: true},
    {
      name: 'ItemName',
      visibleName: 'Item name',
      sortable: true,
    },
    {name: 'Actions', sortable: false}
  ];
  taskWorkerAssignments: Paged<TaskWorkerModel>;
  siteName: string;

  planAreaRuleSub$: Subscription;

  constructor(
    public taskWorkerAssignmentsStateService: TaskWorkerAssignmentsStateService,
    private activatedRoute: ActivatedRoute,
    private sitesService: SitesService,
    private backendConfigurationPnAreasService: BackendConfigurationPnAreasService,
    private areasService: BackendConfigurationPnAreasService,
  ) {
  }

  ngOnInit() {
    this.activatedRoute.params.subscribe((params) => {
      this.taskWorkerAssignmentsStateService.siteId = +params['siteId'];
      this.getSiteName();
      this.getTaskWorkerAssignments();
    });
  }

  sortTable(sort: string) {
    this.taskWorkerAssignmentsStateService.onSortTable(sort);
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
    this.sitesService
      .getSingleSite(this.taskWorkerAssignmentsStateService.siteId)
      .subscribe((data) => {
        if (data && data.success && data.model) {
          this.siteName = data.model.siteName;
        }
      })
  }

  openAreaRulePlanModal(taskWorkerAssignments: TaskWorkerModel) {
    this.backendConfigurationPnAreasService.getAreaRulePlanningByPlanningId(taskWorkerAssignments.id)
      .subscribe((areaRulePlan) => {
        if (areaRulePlan && areaRulePlan.success && areaRulePlan.model) {
          this.backendConfigurationPnAreasService.getAreaByRuleId(areaRulePlan.model.ruleId)
            .subscribe((area) => {
              if (area && area.success && area.model) {
                this.areaRulePlanModal
                  .show(taskWorkerAssignments.areaRule, taskWorkerAssignments.propertyId, area.model, areaRulePlan.model);
              }
            });
        }
      });
  }

  onUpdateAreaRulePlan(rulePlanning: AreaRulePlanningModel) {
    this.planAreaRuleSub$ = this.areasService
      .updateAreaRulePlanning(rulePlanning)
      .subscribe((data) => {
        if (data && data.success) {
          this.areaRulePlanModal.hide();
        }
      });
  }

  ngOnDestroy(): void {
  }
}
