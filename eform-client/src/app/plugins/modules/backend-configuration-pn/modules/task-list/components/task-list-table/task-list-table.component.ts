import {Component, EventEmitter, Input, NgZone, Output} from '@angular/core';
import {TranslateService} from '@ngx-translate/core';
import {MtxGridColumn} from '@ng-matero/extensions/grid';
import {CommonDictionaryModel, SharedTagModel} from 'src/app/common/models';
import {CalendarBoardModel, CalendarTaskModel} from '../../../../models/calendar';
import {CalendarRepeatService} from '../../../calendar/services/calendar-repeat.service';
import {formatRepeatText} from '../../../calendar-task-list/calendar-task-list-repeat.util';

@Component({
  selector: 'app-task-list-table',
  templateUrl: './task-list-table.component.html',
  styleUrls: ['./task-list-table.component.scss'],
  standalone: false,
})
export class TaskListTableComponent {
  @Input() tasks: CalendarTaskModel[] = [];
  @Input() properties: CommonDictionaryModel[] = [];
  @Input() boards: CalendarBoardModel[] = [];
  @Input() eforms: {id: number; label: string}[] = [];
  @Input() planningTags: SharedTagModel[] = [];
  @Output() editTask = new EventEmitter<CalendarTaskModel>();
  @Output() selectionChanged = new EventEmitter<number[]>();
  /**
   * #1126 — the row asks to be renamed; the PAGE owns the API call, because it
   * also owns `loadTasks()` and the service. The result comes back through
   * `renameSucceeded()` / `renameFailed()` below rather than through the
   * emitter, so this stays a plain `{id, title}` payload.
   */
  @Output() renameTask = new EventEmitter<{id: number; title: string}>();

  showAll = false;

  // ----- Inline rename state (#1126) --------------------------------------
  // Shape taken from AdhocAreaAdminModalComponent: one `editingId` gates which
  // row is in edit mode, one `busy` disables the input while the call is in
  // flight, and one `errorKey` renders inline WITHOUT closing the editor, so a
  // failed save can be retried with the typed text still there.
  editingId: number | null = null;
  editTitle = '';
  errorKey: string | null = null;
  busy = false;
  // The title as it was when editing started. Compared against on save so an
  // unchanged value closes the editor with NO API call, and restored by Esc.
  private originalTitle = '';

  constructor(
    private translate: TranslateService,
    private repeatService: CalendarRepeatService,
    private zone: NgZone,
  ) {}

  propertyName = (id: number | null | undefined): string =>
    id == null ? '' : (this.properties.find(p => p.id === id)?.name ?? '');
  board = (id: number | null | undefined): CalendarBoardModel | undefined =>
    id == null ? undefined : this.boards.find(b => b.id === id);
  eformLabel = (id: number | null | undefined): string =>
    id == null ? '' : (this.eforms.find(e => e.id === id)?.label ?? '');
  planningTagName = (id: number | null | undefined): string =>
    id == null ? '' : (this.planningTags.find(t => t.id === id)?.name ?? '');

  boardColor(task: CalendarTaskModel): string {
    return task.color || this.board(task.boardId)?.color || 'transparent';
  }

  repeatText(task: CalendarTaskModel): string {
    return formatRepeatText(this.repeatService, this.translate, task);
  }

  // Converts the row's `taskDate` ("yyyy-MM-dd") to "dd-MM-yyyy" by splitting
  // and reordering (no `new Date()` parsing, which could shift across timezones).
  formatStartDate(value: string): string {
    if (!value) {
      return '';
    }
    const [y, m, d] = value.split('-');
    return d && m && y ? `${d}-${m}-${y}` : '';
  }

  // `[sortOnFront]="true"` (see the .html) means mtx-grid sorts client-side
  // via MatTableDataSource's default `data[sortHeaderId]` accessor, where
  // `sortHeaderId = col.sortProp?.id || col.field` (mtx-grid template). Do
  // NOT set `sortProp.id` to a PascalCase server-sort key here — there is no
  // server-side sort for this grid, and a `sortProp.id` that doesn't match
  // the row's actual (camelCase) property name makes clicking that header a
  // silent no-op (data[mismatchedKey] is undefined for every row, so the
  // comparator treats all rows as equal and the array order never changes).
  columns: MtxGridColumn[] = [
    {
      field: 'id', header: this.translate.stream('Id'), sortable: true,
      formatter: (t: CalendarTaskModel) =>
        `${t.id} <small class="microting-uid">(${t.planningId ?? ''})</small>`,
    },
    {
      field: 'property', header: this.translate.stream('Property'),
      formatter: (t: CalendarTaskModel) => this.propertyName(t.propertyId),
    },
    {field: 'board', header: this.translate.stream('Calendar')},
    {
      field: 'overskrift', header: this.translate.stream('Report headline'),
      formatter: (t: CalendarTaskModel) => this.planningTagName(t.itemPlanningTagId),
    },
    {field: 'title', header: this.translate.stream('Task name'), sortable: true},
    {
      field: 'eform', header: this.translate.stream('eForm'),
      formatter: (t: CalendarTaskModel) => this.eformLabel(t.eformId),
    },
    {
      field: 'assignedTo', header: this.translate.stream('Assigned to'),
      formatter: (t: CalendarTaskModel) => (t.workerNames ?? []).join('<br/>'),
    },
    {
      field: 'tags', header: this.translate.stream('Tags'),
      formatter: (t: CalendarTaskModel) => (t.tags ?? []).join(', '),
    },
    {
      field: 'taskDate', header: this.translate.stream('Start date'), sortable: true,
      formatter: (t: CalendarTaskModel) => this.formatStartDate(t.taskDate),
    },
    {
      field: 'repeat', header: this.translate.stream('Repeat'),
      formatter: (t: CalendarTaskModel) => this.repeatText(t),
    },
    {field: 'status', header: this.translate.stream('Active'), sortable: true},
    {field: 'compliance', header: this.translate.stream('Compliance')},
  ];

  onEdit(task: CalendarTaskModel) {
    this.editTask.emit(task);
  }

  // ----- Inline rename (#1126) --------------------------------------------

  /**
   * mtx-grid binds `(click)="_selectRow(...)"` on the `<tr>` itself, and with
   * `[rowSelectable]` set that handler CLEARS the whole selection and toggles
   * the clicked row (mtxGrid.mjs `_selectRow`). Every interactive element of
   * the title cell therefore has to stop the click from reaching the row, or
   * clicking into the editor would silently wipe a batch selection.
   */
  stopRowClick(event: Event) {
    event.stopPropagation();
  }

  startRename(task: CalendarTaskModel, event: Event) {
    this.stopRowClick(event);
    // Ignore a second click while a save is in flight: `busy` belongs to the
    // one open editor, and switching rows mid-save would strand it.
    if (this.busy) {
      return;
    }
    this.editingId = task.id;
    this.editTitle = task.title ?? '';
    this.originalTitle = task.title ?? '';
    this.errorKey = null;
    this.focusEditor(task.id);
  }

  /**
   * Autofocus + select-all. The input lives inside an mtx-grid `cellTemplate`
   * `<ng-template>` that is instantiated per row, so there is no stable
   * `@ViewChild` to target — the element is looked up by its own id once
   * Angular has rendered it. `setTimeout` (outside Angular, so it does not
   * schedule an extra change-detection pass) runs after the current CD cycle
   * has created the input.
   */
  private focusEditor(taskId: number) {
    this.zone.runOutsideAngular(() => {
      setTimeout(() => {
        const el = document.getElementById(`taskListTitleInput-${taskId}`) as HTMLInputElement | null;
        if (el) {
          el.focus();
          el.select();
        }
      });
    });
  }

  onRenameKeydown(event: KeyboardEvent, task: CalendarTaskModel) {
    // Keep Enter/Escape (and every other key) inside the cell. Escape in
    // particular bubbles to any CDK overlay/dialog ancestor and would close it.
    event.stopPropagation();
    if (event.key === 'Enter') {
      event.preventDefault();
      this.saveRename(task);
    } else if (event.key === 'Escape') {
      event.preventDefault();
      this.cancelRename();
    }
  }

  /**
   * Blur saves, exactly like Enter, so clicking elsewhere never silently
   * discards a typed name.
   *
   * Two blurs must NOT save, and both are filtered here:
   *  - the blur fired while a save is already in flight (`busy`);
   *  - the blur that some browsers fire when the focused input is detached,
   *    which happens right after Esc/save closed the editor — by then
   *    `editingId` no longer names this row, so `saveRename` no-ops.
   */
  onRenameBlur(task: CalendarTaskModel) {
    if (this.editingId !== task.id || this.busy) {
      return;
    }
    this.saveRename(task);
  }

  saveRename(task: CalendarTaskModel) {
    if (this.editingId !== task.id || this.busy) {
      return;
    }
    const title = (this.editTitle ?? '').trim();
    if (!title) {
      // Same rule as the edit modal's `Validators.required` title control:
      // an empty name is not a way to clear the field, it is invalid input.
      // Stay in edit mode and re-focus so the user can just type.
      this.errorKey = 'Task name is required';
      this.focusEditor(task.id);
      return;
    }
    if (title === (this.originalTitle ?? '').trim()) {
      // Nothing changed — close without touching the server. Deliberate: the
      // rename goes through UpdateTask, which is far from a no-op (it rewrites
      // the planning and re-derives its recurrence fields).
      this.cancelRename();
      return;
    }
    this.busy = true;
    this.errorKey = null;
    this.renameTask.emit({id: task.id, title});
  }

  cancelRename() {
    this.editingId = null;
    this.editTitle = '';
    this.originalTitle = '';
    this.errorKey = null;
  }

  /** Called by the page when the rename round-trip succeeded. */
  renameSucceeded() {
    this.busy = false;
    this.cancelRename();
  }

  /**
   * Called by the page when the rename round-trip failed. Keeps `editingId`
   * set — the AdhocAreaAdminModal contract — so the typed text survives and the
   * user can correct and retry. The server's own reason is already on screen as
   * a toast (`apiBaseService.post`); this is the in-context marker.
   */
  renameFailed() {
    this.busy = false;
    this.errorKey = 'Failed to rename task';
    if (this.editingId != null) {
      this.focusEditor(this.editingId);
    }
  }

  onRowSelected(rows: CalendarTaskModel[]) {
    this.selectionChanged.emit((rows ?? []).map(r => r.id));
  }

  toggleShowAll() {
    this.showAll = !this.showAll;
    // Flipping [pageOnFront]/[showPaginator] rebinds template inputs on mtx-grid,
    // which rebuilds its internal SelectionModel EMPTY in ngOnChanges without
    // emitting rowSelectedChange. Emit an empty selection ourselves so the page's
    // `selection` Set (and the batch-dropdown/counter it drives) stays in sync.
    this.selectionChanged.emit([]);
  }
}
