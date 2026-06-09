import {Component, OnInit} from '@angular/core';
import {MatDialog} from '@angular/material/dialog';
import {Overlay} from '@angular/cdk/overlay';
import {TranslateService} from '@ngx-translate/core';
import {of} from 'rxjs';
import {dialogConfigHelper} from 'src/app/common/helpers';
import {CommonDictionaryModel, SharedTagModel, TemplateRequestModel} from 'src/app/common/models';
import {EFormService} from 'src/app/common/services';
import {
  CalendarBoardModel,
  CalendarTaskListFiltrationModel,
  CalendarTaskModel,
} from '../../../../models/calendar';
import {
  BackendConfigurationPnCalendarService,
  BackendConfigurationPnPropertiesService,
} from '../../../../services';
import {ItemsPlanningPnTagsService} from 'src/app/plugins/modules/items-planning-pn/services';
import {CalendarRepeatService} from '../../../calendar/services/calendar-repeat.service';
import {getCurrentLocale} from '../../../calendar/services/calendar-locale.helper';
import {mapResponseToCalendarTask} from '../../../calendar/services/calendar-task.mapper';
import {
  TaskCreateEditModalComponent,
  TaskCreateEditModalData,
} from '../../../calendar/modals/task-create-edit-modal/task-create-edit-modal.component';

@Component({
  selector: 'app-calendar-task-list-page',
  templateUrl: './calendar-task-list-page.component.html',
  styleUrls: ['./calendar-task-list-page.component.scss'],
  standalone: false,
})
export class CalendarTaskListPageComponent implements OnInit {
  properties: CommonDictionaryModel[] = [];
  boards: CalendarBoardModel[] = [];
  workers: CommonDictionaryModel[] = [];
  eforms: {id: number; label: string}[] = [];
  tags: SharedTagModel[] = [];
  tasks: CalendarTaskModel[] = [];

  private currentFilters: CalendarTaskListFiltrationModel = {
    propertyIds: [], boardIds: [], eformIds: [], assignToIds: [],
    tagIds: [], status: null, complianceEnabled: null, nameFilter: null,
  };

  constructor(
    private dialog: MatDialog,
    private overlay: Overlay,
    private translate: TranslateService,
    private calendarService: BackendConfigurationPnCalendarService,
    private propertiesService: BackendConfigurationPnPropertiesService,
    private tagsService: ItemsPlanningPnTagsService,
    private eformService: EFormService,
    private repeatService: CalendarRepeatService,
  ) {}

  ngOnInit() {
    this.loadProperties();
    this.loadTags();
    this.loadEforms();
    this.loadTasks();
  }

  loadProperties() {
    this.propertiesService.getAllPropertiesDictionary().subscribe(res => {
      if (res && res.success) {
        this.properties = res.model;
      }
    });
  }

  loadTags() {
    this.tagsService.getPlanningsTags().subscribe(res => {
      if (res && res.success) {
        this.tags = res.model;
      }
    });
  }

  loadEforms() {
    const req = new TemplateRequestModel();
    req.sort = 'Id';
    req.isSortDsc = false;
    req.pageSize = 1000;
    this.eformService.getAll(req).subscribe(res => {
      if (res && res.success && res.model) {
        this.eforms = res.model.templates.map(t => ({id: t.id, label: t.label}));
      }
    });
  }

  loadBoards(propertyId: number) {
    this.calendarService.getBoards(propertyId).subscribe(res => {
      if (res && res.success) {
        this.boards = res.model;
      }
    });
  }

  loadWorkers(propertyId: number) {
    this.propertiesService.getDeviceUsersFiltered({
      propertyIds: [propertyId],
      nameFilter: '',
      sort: 'Name',
      isSortDsc: false,
      showResigned: false,
      tagIds: [],
    }).subscribe(res => {
      if (res && res.success) {
        this.workers = res.model.map(u => ({
          id: u.siteId,
          name: u.fullName || `${u.userFirstName} ${u.userLastName}`.trim() || u.siteName,
          description: '',
        } as CommonDictionaryModel));
      }
    });
  }

  onFiltersChanged(filters: CalendarTaskListFiltrationModel) {
    this.currentFilters = filters;
    this.loadTasks();
  }

  onPropertyChanged(propertyId: number | null) {
    this.boards = [];
    this.workers = [];
    if (propertyId != null) {
      this.loadBoards(propertyId);
      this.loadWorkers(propertyId);
    }
  }

  loadTasks() {
    this.calendarService.getTasksIndex({
      filters: this.currentFilters,
      pagination: {sort: 'Id', isSortDsc: false},
    }).subscribe(res => {
      if (res && res.success) {
        // The index endpoint returns the raw AreaRulePlanning projection (repeat
        // integers, no `repeatRule`). Map each row exactly as the calendar week
        // grid does so the humanized Gentagelse + modal `data.task` are identical.
        this.tasks = (res.model ?? []).map(mapResponseToCalendarTask);
      }
    });
  }

  onEditTask(task: CalendarTaskModel) {
    const data: TaskCreateEditModalData = {
      task,
      date: task.taskDate,
      startHour: task.startHour,
      boards: this.boards,
      selectedBoardId: task.boardId ?? undefined,
      employees: this.workers,
      tags: this.tags.map(t => t.name),
      propertyId: task.propertyId,
      properties: this.properties,
      eforms: of(this.eforms),
      folderId: null,
      planningTags: this.tags.map(t => ({id: t.id, name: t.name})),
    };
    const ref = this.dialog.open(TaskCreateEditModalComponent, {
      ...dialogConfigHelper(this.overlay, data),
      minWidth: 1024,
    });
    ref.afterClosed().subscribe(result => {
      if (result) {
        this.loadTasks();
      }
    });
  }

  exportCsv() {
    const headers = ['Id', 'Property', 'Board', 'Report headline', 'Task name', 'eForm',
      'Assigned to', 'Tags', 'Repeat', 'Active', 'Compliance']
      .map(h => this.translate.instant(h));
    const rows = this.tasks.map(t => [
      t.id,
      this.properties.find(p => p.id === t.propertyId)?.name ?? '',
      this.boards.find(b => b.id === t.boardId)?.name ?? '',
      this.tags.find(x => x.id === t.itemPlanningTagId)?.name ?? '',
      t.title ?? '',
      this.eforms.find(e => e.id === t.eformId)?.label ?? '',
      (t.workerNames ?? []).join(', '),
      (t.tags ?? []).join(', '),
      this.repeatTextForCsv(t),
      this.translate.instant(t.status ? 'Yes' : 'No'),
      this.translate.instant(t.complianceEnabled ? 'Yes' : 'No'),
    ]);
    const esc = (v: unknown) => {
      const s = String(v ?? '');
      return /[";\n]/.test(s) ? `"${s.replace(/"/g, '""')}"` : s;
    };
    const csv = headers.join(';') + '\n' + rows.map(r => r.map(esc).join(';')).join('\n');
    const blob = new Blob([new Uint8Array([0xEF, 0xBB, 0xBF]), csv], {type: 'text/csv;charset=utf-8;'});
    const link = document.createElement('a');
    link.href = URL.createObjectURL(blob);
    link.download = 'opgaver-og-handlinger.csv';
    link.click();
    URL.revokeObjectURL(link.href);
  }

  private repeatTextForCsv(t: CalendarTaskModel): string {
    if (!t.repeatRule || t.repeatRule === 'none') {
      return this.translate.instant('Does not repeat');
    }
    const meta = this.repeatService.reconstructMetaFromTask(t);
    return meta ? this.repeatService.formatCustomRepeatLabel(meta, getCurrentLocale(this.translate)) : t.repeatRule;
  }
}
