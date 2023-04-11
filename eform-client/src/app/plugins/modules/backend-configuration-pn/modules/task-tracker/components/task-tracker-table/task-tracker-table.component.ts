import {
  Component,
  EventEmitter,
  Input,
  OnInit,
  Output,
} from '@angular/core';
import {TaskModel} from '../../../../models';
import {
  TaskTrackerStateService
} from '../store';
import {MtxGridColumn} from '@ng-matero/extensions/grid';
import {TranslateService} from '@ngx-translate/core';
import * as R from 'ramda';
import {addDays, differenceInDays, differenceInWeeks, eachWeekOfInterval, getDay, getWeek} from 'date-fns';

@Component({
  selector: 'app-task-tracker-table',
  templateUrl: './task-tracker-table.component.html',
  styleUrls: ['./task-tracker-table.component.scss'],
})
export class TaskTrackerTableComponent implements OnInit {
  tableHeaders: MtxGridColumn[] = [
    {header: this.translateService.stream('Property'), field: 'property'},
    {header: this.translateService.stream('Task'), field: 'taskName'},
    {header: this.translateService.stream('Tags'), field: 'tags'},
    {header: this.translateService.stream('Workers'), field: 'workers', formatter: (row: TaskModel) => row.workers.join(', ')},
    {
      header: this.translateService.stream('Start'),
      field: 'startTask',
      type: 'date',
      typeParameter: {format: 'dd.MM.yyyy'},
    },
    {header: this.translateService.stream('Repeat'), field: 'repeat'},
    {
      header: this.translateService.stream('Deadline'),
      field: 'deadlineTask',
      type: 'date',
      typeParameter: {format: 'dd.MM.yyyy'},
    },
  ];

  @Input() tasks: TaskModel[] = [];
  @Output() updateTable: EventEmitter<void> = new EventEmitter<void>();
  days: Date[] = [];
  daysInTable: number[] = [];
  weeks: { weekNumber: number, length: number }[] = [];

  constructor(
    private translateService: TranslateService,
  ) {
  }

  ngOnInit(): void {
    const currentDate = new Date();
    this.days = [...R.range(0, 30)].map((x: number): Date => addDays(currentDate, x));
    this.daysInTable = this.days.map(x => x.getDate());
    let weeks = eachWeekOfInterval({start: currentDate, end: addDays(currentDate, 30)}, {weekStartsOn: 2});

    for (let i = 1; i < weeks.length; i++) {
      if (i === 1) {
        const difference = differenceInDays(weeks[i], currentDate);
        if (difference !== 7) {
          weeks = [...weeks, addDays(weeks[weeks.length - 1], difference)];
        }
        this.weeks = [...this.weeks, {weekNumber: getWeek(weeks[i]), length: difference}];
      } else {
        const difference = differenceInDays(weeks[i], weeks[i - 1]);
        this.weeks = [...this.weeks, {weekNumber: getWeek(weeks[i]), length: difference}];
      }
    }
  }

  getDayOfWeekByDay(day: number): string {
    const index = this.days.findIndex(x => x.getDate() === day);
    if(index !== -1) {
      const dateFnsDayOfWeek = getDay(this.days[index]);
      switch (dateFnsDayOfWeek) {
        case 0:
          return 'Su'
        case 1:
          return 'Mo'
        case 2:
          return 'Tu'
        case 3:
          return 'We'
        case 4:
          return 'Th'
        case 5:
          return 'Fr'
        case 6:
          return 'Sa'
      }
    }
    return '';
  }
}
