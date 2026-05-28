import {Component, EventEmitter, Input, OnChanges, Output, inject} from '@angular/core';
import {TranslateService} from '@ngx-translate/core';
import {CalendarBoardModel, CalendarTaskLayoutModel, CalendarToggleCompleteResult} from '../../../../models/calendar';
import {getCurrentLocale} from '../../services/calendar-locale.helper';
import {BackendConfigurationPnCalendarService} from '../../../../services';

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
  @Output() taskClicked = new EventEmitter<{task: CalendarTaskLayoutModel; cellLeft: number; cellRight: number; slotTop: number}>();
  @Output() completeRequiresForm = new EventEmitter<CalendarToggleCompleteResult>();

  groups: ScheduleGroup[] = [];

  private calendarService = inject(BackendConfigurationPnCalendarService);

  constructor(private translate: TranslateService) {}

  onCompletionClick(task: CalendarTaskLayoutModel, event: MouseEvent) {
    event.stopPropagation();
    // Match the week-grid indicator: the SDK case status is one-way, so
    // once an event is completed the indicator is inert (no doomed PUT).
    if (task.completed) return;
    this.calendarService
      .toggleComplete(task.id, !task.completed, task.complianceId, task.taskDate)
      .subscribe(res => {
        if (!res?.success) return;
        if (res.model?.requiresForm) {
          this.completeRequiresForm.emit(res.model);
          return;
        }
        this.tasksReload.emit();
      });
  }

  ngOnChanges() {
    this.buildGroups();
  }

  // Mirrors the week-grid `taskClicked` payload so the container can
  // route both views through the same `onTaskClickedFromGrid` handler
  // (preview popover with Edit / Duplicate / Delete buttons).
  onTaskClicked(task: CalendarTaskLayoutModel, event: MouseEvent) {
    const row = (event.currentTarget as HTMLElement).getBoundingClientRect();
    this.taskClicked.emit({
      task,
      cellLeft: row.left,
      cellRight: row.right,
      slotTop: row.top,
    });
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
