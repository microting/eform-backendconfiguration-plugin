import { Component, OnDestroy, OnInit, ViewChild } from '@angular/core';
import { AutoUnsubscribe } from 'ngx-auto-unsubscribe';
import {
  Paged,
  TableHeaderElementModel,
} from 'src/app/common/models';
import {TaskWorkerAssignmentsStateService} from '../store';
import {TaskWorkerModel} from '../../../../models';
import {ActivatedRoute, Router} from '@angular/router';
import {SitesService} from 'src/app/common/services';

@AutoUnsubscribe()
@Component({
  selector: 'app-property-worker-assignments-page',
  templateUrl: './task-worker-assignments-page.component.html',
})
export class TaskWorkerAssignmentsPageComponent implements OnInit, OnDestroy {
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

  constructor(
    public taskWorkerAssignmentsStateService: TaskWorkerAssignmentsStateService,
    private activatedRoute: ActivatedRoute,
    private sitesService: SitesService,
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
        if (data && data.success && data.model){
          this.taskWorkerAssignments = data.model;
        }
      })
  }

  getSiteName() {
    this.sitesService
      .getSingleSite(this.taskWorkerAssignmentsStateService.siteId)
      .subscribe((data) => {
        if (data && data.success && data.model){
          this.siteName = data.model.siteName;
        }
      })
  }

  ngOnDestroy(): void {}
}
