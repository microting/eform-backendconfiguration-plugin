import {Component, EventEmitter, Input, Output} from '@angular/core';
import {TranslateService} from '@ngx-translate/core';
import {MtxGridColumn} from '@ng-matero/extensions/grid';
import {CommonDictionaryModel, SharedTagModel} from 'src/app/common/models';
import {CalendarBoardModel, CalendarTaskModel} from '../../../../models/calendar';
import {CalendarRepeatService} from '../../../calendar/services/calendar-repeat.service';
import {formatRepeatText} from '../../calendar-task-list-repeat.util';

@Component({
  selector: 'app-calendar-task-list-table',
  templateUrl: './calendar-task-list-table.component.html',
  styleUrls: ['./calendar-task-list-table.component.scss'],
  standalone: false,
})
export class CalendarTaskListTableComponent {
  @Input() tasks: CalendarTaskModel[] = [];
  @Input() properties: CommonDictionaryModel[] = [];
  @Input() boards: CalendarBoardModel[] = [];
  @Input() eforms: {id: number; label: string}[] = [];
  @Input() planningTags: SharedTagModel[] = [];
  @Output() editTask = new EventEmitter<CalendarTaskModel>();

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

  columns: MtxGridColumn[] = [
    {
      field: 'id', header: this.translate.stream('Id'), sortable: true, sortProp: {id: 'Id'},
      formatter: (t: CalendarTaskModel) =>
        `${t.id} <small class="microting-uid">(${t.planningId ?? ''})</small>`,
    },
    {
      field: 'property', header: this.translate.stream('Property'),
      formatter: (t: CalendarTaskModel) => this.propertyName(t.propertyId),
    },
    {field: 'board', header: this.translate.stream('Board')},
    {
      field: 'overskrift', header: this.translate.stream('Report headline'),
      formatter: (t: CalendarTaskModel) => this.planningTagName(t.itemPlanningTagId),
    },
    {field: 'title', header: this.translate.stream('Task name'), sortable: true, sortProp: {id: 'Title'}},
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
    {field: 'status', header: this.translate.stream('Active'), sortable: true, sortProp: {id: 'Status'}},
    {field: 'compliance', header: this.translate.stream('Compliance')},
  ];

  onEdit(task: CalendarTaskModel) {
    this.editTask.emit(task);
  }
}
