import {
  Component,
  EventEmitter,
  Input, OnChanges,
  OnInit,
  Output, SimpleChanges,
} from '@angular/core';
import {Columns, ColumnsModel, TaskModel} from '../../../../models';
import {TranslateService} from '@ngx-translate/core';
import * as R from 'ramda';
import {TaskTrackerStateService} from '../store';
import {
  addDays,
  differenceInDays,
  getDay,
  getWeek,
  isMonday, nextMonday,
  parseISO,
  set
} from 'date-fns';

@Component({
  selector: 'app-task-tracker-table',
  templateUrl: './task-tracker-table.component.html',
  styleUrls: ['./task-tracker-table.component.scss'],
})
export class TaskTrackerTableComponent implements OnInit, OnChanges {
  @Input() columnsFromDb: Columns = {
    deadline: true,
    property: true,
    repeat: true,
    start: true,
    tags: true,
    task: true,
    workers: true,
  };
  @Input() tasks: TaskModel[] = [];
  @Output() updateTable: EventEmitter<void> = new EventEmitter<void>();

  days: Date[] = [];
  daysInTable: number[] = [];
  weeks: { weekNumber: number, length: number }[] = [];
  enabledHeadersNumber: number = 7;
  propertyHeaderEnabled: boolean = false;

  constructor(
    private translateService: TranslateService,
    private taskTrackerStateService: TaskTrackerStateService,
  ) {
  }

  getRepeatType(repeatType: number) {
    switch (repeatType) {
      case 1:
        return this.translateService.instant('Day');
      case 2:
        return this.translateService.instant('Week');
      case 3:
        return this.translateService.instant('Month');
      default:
        return `--`;
    }
  }

  ngOnInit(): void {
    this.taskTrackerStateService.getFiltersAsync().subscribe(filters => {
      this.propertyHeaderEnabled = filters.propertyIds.some(x => x === -1);
      this.recalculateColumns();
    })
    this.initTable();
  }

  initTable() {
    const currentDate = set(new Date(), {
      hours: 0,
      minutes: 0,
      seconds: 0,
      milliseconds: 0,
    });
    this.days = [...R.range(0, 30)].map((x: number): Date => addDays(currentDate, x));
    this.daysInTable = this.days.map(x => x.getDate());
    let weeks = this.days.map((x, i) => {
      if (i === 0 && !isMonday(x)) {
        return {startDay: x, endDay: nextMonday(x)};
      }
      if (isMonday(x)) {
        return {startDay: x, endDay: nextMonday(x)};
      }
      return undefined;
    }).filter(x => x);

    for (let i = 0; i < weeks.length; i++) {
      const difference = differenceInDays(weeks[i].endDay, weeks[i].startDay);
      this.weeks = [...this.weeks, {weekNumber: getWeek(weeks[i].startDay), length: difference}];
    }
  }

  getDayOfWeekByDay(day: number): string {
    const index = this.days.findIndex(x => x.getDate() === day);
    if (index !== -1) {
      const dateFnsDayOfWeek = getDay(this.days[index]);
      switch (dateFnsDayOfWeek) {
        case 0:
          return 'Sunday';
        case 1:
          return 'Monday';
        case 2:
          return 'Tuesday';
        case 3:
          return 'Wednesday';
        case 4:
          return 'Thursday';
        case 5:
          return 'Friday';
        case 6:
          return 'Saturday';
      }
    }
    return '';
  }

  getColorByDayAndTask(day: number, task: TaskModel): string {
    const index = this.days.findIndex(x => x.getDate() === day);
    if (index !== -1) {
      const setDate = (date: Date) => set(date, {
        hours: 0,
        minutes: 0,
        seconds: 0,
        milliseconds: 0,
      });
      const fullDay = setDate(this.days[index]);
      const nextExecutionTime = setDate(parseISO(task.nextExecutionTime));
      if (fullDay.toISOString() === nextExecutionTime.toISOString()) {
        return 'white-yellow';
      } else {
        return 'yellow';
      }
    }
    return '';
  }

  getRepeatEveryAndRepeatTypeByTask(task: TaskModel): string {
    return `${task.repeatEvery} ${this.getRepeatType(task.repeatType)}`;
  }

  recalculateColumns() {
    let columns: ColumnsModel[] = [];
    if (this.columnsFromDb) {
      this.columnsFromDb.property = this.propertyHeaderEnabled;
      for (let [key, value] of Object.entries(this.columnsFromDb)) {
        if (this.columnsFromDb.hasOwnProperty(key)) {
          columns = [...columns, {columnName: key, isColumnEnabled: value}];
        }
      }
      this.enabledHeadersNumber = columns.filter(x => x.isColumnEnabled).length;
    }
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (changes && changes.columns && !changes.columns.isFirstChange()) {
      this.columnsFromDb = changes.columns.currentValue;
      this.recalculateColumns();
    }
  }
}
