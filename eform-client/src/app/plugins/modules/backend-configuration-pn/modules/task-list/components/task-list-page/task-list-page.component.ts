import {Component, OnInit} from '@angular/core';
import {MatDialog} from '@angular/material/dialog';
import {Overlay} from '@angular/cdk/overlay';
import {TranslateService} from '@ngx-translate/core';
import {of} from 'rxjs';
import {dialogConfigHelper} from 'src/app/common/helpers';
import {CommonDictionaryModel, SharedTagModel, TemplateRequestModel} from 'src/app/common/models';
import {EFormService, EformTagService} from 'src/app/common/services';
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
import {mapResponseToCalendarTask} from '../../../calendar/services/calendar-task.mapper';
import {formatRepeatText} from '../../../calendar-task-list/calendar-task-list-repeat.util';
import {
  TaskCreateEditModalComponent,
  TaskCreateEditModalData,
} from '../../../calendar/modals/task-create-edit-modal/task-create-edit-modal.component';
import {BatchWorkerModalComponent} from '../modals/batch-worker-modal/batch-worker-modal.component';
import {BatchEformModalComponent} from '../modals/batch-eform-modal/batch-eform-modal.component';
import {BatchTagsModalComponent} from '../modals/batch-tags-modal/batch-tags-modal.component';
import {BatchCopyModalComponent} from '../modals/batch-copy-modal/batch-copy-modal.component';
import {BatchDeleteModalComponent} from '../modals/batch-delete-modal/batch-delete-modal.component';

// Task 11 implements the batch action modals; this task only wires up the
// dropdown + selection plumbing and stubs the modal opener.
export type TaskListBatchAction =
  | 'assign'
  | 'reassign'
  | 'addWorker'
  | 'changeEform'
  | 'addTags'
  | 'removeTags'
  | 'copy'
  | 'delete';

interface TaskListBatchActionOption {
  id: TaskListBatchAction;
  label: string;
  group: string;
  disabled: boolean;
}

// Shared MAT_DIALOG_DATA shape for all five batch-action modals (Task 11).
export interface TaskListBatchModalData {
  mode: TaskListBatchAction;
  selectedTasks: CalendarTaskModel[];
  workers?: CommonDictionaryModel[];
  eforms?: {id: number; label: string}[];
  tags?: SharedTagModel[];
  properties?: CommonDictionaryModel[];
}

@Component({
  selector: 'app-task-list-page',
  templateUrl: './task-list-page.component.html',
  styleUrls: ['./task-list-page.component.scss'],
  standalone: false,
})
export class TaskListPageComponent implements OnInit {
  properties: CommonDictionaryModel[] = [];
  boards: CalendarBoardModel[] = [];
  workers: CommonDictionaryModel[] = [];
  // Available worker tags (a.k.a. "teams") for the edit modal's worker-tag field.
  teams: CommonDictionaryModel[] = [];
  eforms: {id: number; label: string}[] = [];
  tags: SharedTagModel[] = [];
  tasks: CalendarTaskModel[] = [];

  selection = new Set<number>();
  pendingAction: TaskListBatchAction | null = null;

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
    private eformTagService: EformTagService,
    private repeatService: CalendarRepeatService,
  ) {}

  ngOnInit() {
    this.loadProperties();
    this.loadTags();
    this.loadWorkerTags();
    this.loadEforms();
    this.loadTasks();
  }

  loadWorkerTags() {
    this.eformTagService.getAvailableTags().subscribe(res => {
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
        // Selection references rows from the previous load; clear it on refresh.
        this.selection = new Set<number>();
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
      workerTags: this.teams,
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

  onSelectionChanged(ids: number[]) {
    this.selection = new Set(ids);
  }

  // Property-scoped actions (assign/reassign/addWorker/copy) require exactly one
  // selected property in the current filters — the option-lists they rely on
  // (workers, target property) are otherwise ambiguous or unavailable.
  private get singleSelectedPropertyId(): number | null {
    return this.currentFilters.propertyIds.length === 1 ? this.currentFilters.propertyIds[0] : null;
  }

  // Memoized on the property-scope + current-language key: `batchActions` is read
  // directly from the template ([items]="batchActions"), so without caching this
  // getter reruns on every change-detection tick and returns a fresh array/object
  // literals each time. mtx-select (ng-select) treats that as the `items`
  // input changing identity and tears down + rebuilds its option list
  // continuously — in practice this happens fast enough that a real mouse
  // click on an option never lands (the DOM node it targeted is gone before
  // the click completes; discovered while browser-testing Task 11's batch
  // modals — the dropdown was unusable by mouse without this).
  private _batchActionsCache: TaskListBatchActionOption[] | null = null;
  private _batchActionsCacheKey: string | null = null;

  get batchActions(): TaskListBatchActionOption[] {
    const key = `${this.singleSelectedPropertyId}|${this.translate.currentLang}`;
    if (this._batchActionsCache && this._batchActionsCacheKey === key) {
      return this._batchActionsCache;
    }
    const propertyScoped = this.singleSelectedPropertyId == null;
    const employees = this.translate.instant('Employees');
    const tasksGroup = this.translate.instant('Tasks');
    const deleteGroup = this.translate.instant('Delete');
    // Mockup rule: all 8 options are always shown, grouped exactly as the mockup's
    // three optgroups (Medarbejdere / Opgaver / Slet). Property-scoped actions
    // (assign/reassign/addWorker/copy) are disabled — not removed — when no single
    // property is filtered, since their option-lists (workers, target property) are
    // otherwise ambiguous or unavailable. ng-select reads `.disabled` directly off
    // each bound item, so disabling in place (rather than filtering) is sufficient
    // for it to render them grayed and non-selectable.
    const all: TaskListBatchActionOption[] = [
      {id: 'assign', label: this.translate.instant('Move selected to employee'), group: employees, disabled: propertyScoped},
      {id: 'reassign', label: this.translate.instant('Move from employee to employee'), group: employees, disabled: propertyScoped},
      {id: 'addWorker', label: this.translate.instant('Add employee'), group: employees, disabled: propertyScoped},
      {id: 'changeEform', label: this.translate.instant('Change eForm'), group: tasksGroup, disabled: false},
      {id: 'addTags', label: this.translate.instant('Add tags'), group: tasksGroup, disabled: false},
      {id: 'removeTags', label: this.translate.instant('Remove tags'), group: tasksGroup, disabled: false},
      {id: 'copy', label: this.translate.instant('Copy to property'), group: tasksGroup, disabled: propertyScoped},
      {id: 'delete', label: this.translate.instant('Delete selected'), group: deleteGroup, disabled: false},
    ];
    this._batchActionsCache = all;
    this._batchActionsCacheKey = key;
    return this._batchActionsCache;
  }

  onBatchActionPicked(action: {id: TaskListBatchAction; disabled?: boolean} | TaskListBatchAction | null) {
    if (action && typeof action === 'object' && action.disabled) {
      // Defensive: ng-select already refuses to select disabled items, but guard
      // here too in case a disabled id is ever picked programmatically.
      this.pendingAction = null;
      return;
    }
    const id = typeof action === 'object' ? action?.id : action;
    if (!id) {
      return;
    }
    // NB: the dropdown keeps showing the picked action while its modal is
    // open; it resets to the placeholder in `openBatchModal`'s afterClosed
    // (below). Resetting here — synchronously OR on a microtask — does NOT
    // clear the ng-select in the DOM: this handler runs inside ng-select's
    // own (change) emission and ng-select re-applies its just-committed
    // selectedItems afterwards, so the value label sticks behind the modal
    // (verified from CI shard-y DG3 trace + video across two rounds). The
    // functional guarantee (a repeat pick of the same action re-fires
    // (change)) is provided by the afterClosed reset, which clears
    // ng-select's internal selection cleanly once no overlay is in the way.
    this.openBatchModal(id);
  }

  openBatchModal(action: TaskListBatchAction) {
    const selectedTasks = this.tasks.filter(t => this.selection.has(t.id));
    if (selectedTasks.length === 0) {
      return;
    }
    let data: TaskListBatchModalData = {mode: action, selectedTasks};
    let component: any;
    switch (action) {
      case 'assign':
      case 'reassign':
      case 'addWorker':
        data = {...data, workers: this.workers};
        component = BatchWorkerModalComponent;
        break;
      case 'changeEform':
        data = {...data, eforms: this.eforms};
        component = BatchEformModalComponent;
        break;
      case 'addTags':
        data = {...data, tags: this.tags};
        component = BatchTagsModalComponent;
        break;
      case 'removeTags': {
        // Union of tag names present on any selected row, resolved back to their
        // full tag models (the modal needs id + name for the mtx-select).
        const namesOnSelection = new Set<string>();
        selectedTasks.forEach(t => (t.tags ?? []).forEach(name => namesOnSelection.add(name)));
        data = {...data, tags: this.tags.filter(tag => namesOnSelection.has(tag.name))};
        component = BatchTagsModalComponent;
        break;
      }
      case 'copy':
        data = {...data, properties: this.properties};
        component = BatchCopyModalComponent;
        break;
      case 'delete':
        component = BatchDeleteModalComponent;
        break;
    }
    const ref = this.dialog.open(component, dialogConfigHelper(this.overlay, data));
    ref.afterClosed().subscribe(result => {
      // Reset the batch dropdown to its placeholder once the modal closes
      // (whether confirmed or cancelled), ready for the next pick. Done here
      // rather than on pick because the modal/overlay is gone by now, so
      // writeValue(null) cleanly clears BOTH the ng-select's displayed value
      // AND its internal selectedItems — the latter is what lets a repeat
      // pick of the SAME action fire (change) again instead of being a
      // no-op (CI shard-y DG3).
      this.pendingAction = null;
      if (result) {
        // The modal itself relies on the underlying service call's built-in
        // toast (see BackendConfigurationPnTaskListService — its methods use
        // apiBaseService.post, which already toasts success/error; a second,
        // manual toast in the modal would double up).
        this.selection.clear();
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
    link.download = 'opgaveliste.csv';
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
