import {Component, EventEmitter, Input, Output} from '@angular/core';
import {CdkDragEnd, CdkDragMove} from '@angular/cdk/drag-drop';
import {CalendarTaskLayoutModel} from '../../../../models/calendar';

export const HOUR_HEIGHT = 52; // px per hour

@Component({
  standalone: false,
  selector: 'app-calendar-task-block',
  templateUrl: './calendar-task-block.component.html',
  styleUrls: ['./calendar-task-block.component.scss'],
})
export class CalendarTaskBlockComponent {
  @Input() task!: CalendarTaskLayoutModel;
  @Input() hourHeight = HOUR_HEIGHT;
  @Input() showId = false;

  @Output() clicked = new EventEmitter<CalendarTaskLayoutModel>();
  @Output() toggleComplete = new EventEmitter<CalendarTaskLayoutModel>();
  @Output() dragMoved = new EventEmitter<CdkDragMove<CalendarTaskLayoutModel>>();
  @Output() dragEnded = new EventEmitter<CdkDragEnd>();

  get isPast(): boolean {
    const d = new Date(this.task.taskDate);
    const endHour = this.task.startHour + this.task.duration;
    d.setHours(Math.floor(endHour), Math.round((endHour % 1) * 60), 0, 0);
    return d < new Date();
  }

  get topPx(): number {
    return this.task.startHour * this.hourHeight;
  }

  get heightPx(): number {
    return Math.max(this.task.duration * this.hourHeight - 4, 20);
  }

  get leftPercent(): number {
    return (this.task._colIndex / this.task._colCount) * 100;
  }

  get widthPercent(): number {
    return (1 / this.task._colCount) * 100 - 1;
  }

  onCompletionClick(event: MouseEvent) {
    event.stopPropagation();
    this.toggleComplete.emit(this.task);
  }
}
