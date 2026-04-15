import {Component, EventEmitter, Input, OnChanges, Output} from '@angular/core';
import {TranslateService} from '@ngx-translate/core';
import {CalendarBoardModel, CalendarTaskLayoutModel} from '../../../../models/calendar';
import {getCurrentLocale} from '../../services/calendar-locale.helper';

interface ScheduleGroup {
  dateLabel: string;
  tasks: CalendarTaskLayoutModel[];
}

@Component({
  standalone: false,
  selector: 'app-calendar-schedule-view',
  templateUrl: './calendar-schedule-view.component.html',
  styleUrls: ['./calendar-schedule-view.component.scss'],
})
export class CalendarScheduleViewComponent implements OnChanges {
  @Input() tasksByDay: CalendarTaskLayoutModel[][] = [];
  @Input() currentDate: string = '';
  @Input() boards: CalendarBoardModel[] = [];

  @Output() tasksReload = new EventEmitter<void>();

  groups: ScheduleGroup[] = [];

  constructor(private translate: TranslateService) {}

  ngOnChanges() {
    this.buildGroups();
  }

  private buildGroups() {
    if (!this.currentDate) return;
    const d = new Date(this.currentDate);
    const day = d.getDay();
    const monday = new Date(d);
    monday.setDate(d.getDate() + (day === 0 ? -6 : 1 - day));
    monday.setHours(0, 0, 0, 0);

    this.groups = this.tasksByDay
      .map((tasks, i) => {
        const date = new Date(monday);
        date.setDate(monday.getDate() + i);
        return {
          dateLabel: date.toLocaleDateString(getCurrentLocale(this.translate), {weekday: 'long', day: 'numeric', month: 'long'}),
          tasks: tasks.slice().sort((a, b) => a.startHour - b.startHour),
        };
      })
      .filter(g => g.tasks.length > 0);
  }
}
