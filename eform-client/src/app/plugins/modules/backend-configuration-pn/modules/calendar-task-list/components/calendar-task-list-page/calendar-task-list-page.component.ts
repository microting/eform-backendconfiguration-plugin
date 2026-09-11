import {Component, OnInit} from '@angular/core';
import {MatDialog} from '@angular/material/dialog';
import {Overlay} from '@angular/cdk/overlay';
import {TranslateService} from '@ngx-translate/core';
import {Observable, of} from 'rxjs';
import {defaultIfEmpty, map} from 'rxjs/operators';
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
  BackendConfigurationPnWorkerTagsService,
} from '../../../../services';
import {ItemsPlanningPnTagsService} from 'src/app/plugins/modules/items-planning-pn/services';
import {CalendarRepeatService} from '../../../calendar/services/calendar-repeat.service';
import {mapResponseToCalendarTask} from '../../../calendar/services/calendar-task.mapper';
import {findLogboegerFolderId} from '../../../calendar/services/logboeger-folder.util';
import {formatRepeatText} from '../../calendar-task-list-repeat.util';
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
  // Available worker tags (a.k.a. "teams") for the edit modal's worker-tag field.
  teams: CommonDictionaryModel[] = [];
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
    private workerTagsService: BackendConfigurationPnWorkerTagsService,
    private repeatService: CalendarRepeatService,
  ) {}

  ngOnInit() {
    this.loadProperties();
    this.loadTags();
    this.loadWorkerTags();
    this.loadEforms();
    this.loadTasks();
  }

  // Worker tags come from the PLUGIN endpoint, not the core
  // `EformTagService.getAvailableTags()`: the SDK keeps worker groups and
  // eForm/template tags in one `Tags` table, so the core list offered template
  // tags here and picking one produced an event that reached nobody (#1213).
  // The plugin endpoint filters server-side to tags that have at least one
  // live worker member; nothing is discarded client-side.
  loadWorkerTags() {
    this.workerTagsService.getWorkerTags().subscribe(res => {
      if (res && res.success) {
        this.teams = res.model;
      }
    });
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

  // Calendar names/colors are resolved from the property-scoped board list, so they only appear
  // when a single property is selected (table swatch falls back to task.color).
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

  /**
   * #1135 — the edit modal puts `folderId` straight into the update payload,
   * where it decides which SDK folder the task's eForm is filed under. This
   * page has no folder picker, so it resolves the property's Logbøger folder
   * the same way `CalendarContainerComponent` does (`getLinkedFolderDtos` +
   * `findFolderByName`). Hard-coding `null` here is what made every save from
   * this page fail server-side.
   *
   * Resolved per TASK property, not per selected filter: the grid can list
   * tasks from several properties at once. Successful lookups are cached for
   * the lifetime of the page (the folder tree does not change while it is
   * open); failures are NOT cached, so the next edit retries.
   *
   * Resolved from the task's property as it stands when the modal OPENS. If the
   * user then switches property inside the modal, `propertyId` follows the
   * editable control while `folderId` does not — pre-existing, identical on the
   * calendar page, and deliberately out of scope for #1135.
   */
  private logboegerFolderIdByProperty = new Map<number, number | null>();

  private resolveLogboegerFolderId(propertyId: number): Observable<number | null> {
    if (!propertyId) {
      return of(null);
    }
    if (this.logboegerFolderIdByProperty.has(propertyId)) {
      return of(this.logboegerFolderIdByProperty.get(propertyId) ?? null);
    }
    return this.propertiesService.getLinkedFolderDtos(propertyId).pipe(
      map(res => {
        if (!res || !res.success) {
          // Never fall back to another property's folder — that would refile
          // this task under a property it does not belong to (#1239). null is
          // the supported "no folder supplied" value: the backend keeps the
          // task's current folder.
          return null;
        }
        const folderId = findLogboegerFolderId(res.model);
        this.logboegerFolderIdByProperty.set(propertyId, folderId);
        return folderId;
      }),
      // HttpErrorInterceptor swallows a hard 4xx/5xx into EMPTY, which
      // completes without emitting — without this the modal would silently
      // never open. The interceptor has already toasted the reason.
      defaultIfEmpty(null),
    );
  }

  onEditTask(task: CalendarTaskModel) {
    this.resolveLogboegerFolderId(task.propertyId)
      .subscribe(folderId => this.openEditTaskModal(task, folderId));
  }

  private openEditTaskModal(task: CalendarTaskModel, folderId: number | null) {
    const data: TaskCreateEditModalData = {
      task,
      date: task.taskDate,
      startHour: task.startHour,
      boards: this.boards,
      selectedBoardId: task.boardId ?? undefined,
      employees: this.workers,
      tags: this.tags.map(t => t.name),
      workerTags: this.teams,
      propertyId: task.propertyId,
      properties: this.properties,
      eforms: of(this.eforms),
      folderId,
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
    const headers = ['Id', 'Property', 'Calendar', 'Report headline', 'Task name', 'eForm',
      'Assigned to', 'Tags', 'Start date', 'Repeat', 'Active', 'Compliance']
      .map(h => this.translate.instant(h));
    const rows = this.tasks.map(t => [
      t.id,
      this.properties.find(p => p.id === t.propertyId)?.name ?? '',
      // Calendar names only resolve when a single property is selected (boards is property-scoped).
      this.boards.find(b => b.id === t.boardId)?.name ?? '',
      this.tags.find(x => x.id === t.itemPlanningTagId)?.name ?? '',
      t.title ?? '',
      this.eforms.find(e => e.id === t.eformId)?.label ?? '',
      (t.workerNames ?? []).join(', '),
      (t.tags ?? []).join(', '),
      this.formatStartDate(t.taskDate),
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
    return formatRepeatText(this.repeatService, this.translate, t);
  }

  // "yyyy-MM-dd" -> "dd-MM-yyyy" (split-and-reorder; no timezone-sensitive Date parsing).
  private formatStartDate(value: string): string {
    if (!value) {
      return '';
    }
    const [y, m, d] = value.split('-');
    return d && m && y ? `${d}-${m}-${y}` : '';
  }
}
