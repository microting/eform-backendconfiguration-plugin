import {Component, EventEmitter, Input, Output} from '@angular/core';
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

  showAll = false;

  constructor(
    private translate: TranslateService,
    private repeatService: CalendarRepeatService,
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
