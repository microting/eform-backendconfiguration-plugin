import {Component, EventEmitter, Input, Output} from '@angular/core';
import {CdkDragMove} from '@angular/cdk/drag-drop';
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

  @Output() clicked = new EventEmitter<CalendarTaskLayoutModel>();
  @Output() toggleComplete = new EventEmitter<CalendarTaskLayoutModel>();
  @Output() dragMoved = new EventEmitter<CdkDragMove<CalendarTaskLayoutModel>>();
  @Output() dragEnded = new EventEmitter<void>();

  get topPx(): number {
    return this.task.startHour * this.hourHeight;
  }

  get heightPx(): number {
    return Math.max(this.task.dur * this.hourHeight - 4, 20);
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
