import {Component, EventEmitter, Input, Output} from '@angular/core';
import {CdkDragEnd, CdkDragMove} from '@angular/cdk/drag-drop';
import {boardTextColor, CalendarTaskLayoutModel} from '../../../../models/calendar';

export const HOUR_HEIGHT = 52; // px per hour

export interface TaskResizePayload {
  task: CalendarTaskLayoutModel;
  edge: 'start' | 'end';
  newStartHour: number;
  newDuration: number;
}

@Component({
  standalone: false,
  selector: 'app-calendar-task-block',
  templateUrl: './calendar-task-block.component.html',
  styleUrls: ['./calendar-task-block.component.scss'],
})
export class CalendarTaskBlockComponent {
  readonly boardTextColor = boardTextColor;

  @Input() task!: CalendarTaskLayoutModel;
  @Input() hourHeight = HOUR_HEIGHT;
  @Input() showId = false;
  @Input() raised = false;

  @Output() clicked = new EventEmitter<CalendarTaskLayoutModel>();
  @Output() raiseRequested = new EventEmitter<CalendarTaskLayoutModel>();
  @Output() toggleComplete = new EventEmitter<CalendarTaskLayoutModel>();
  @Output() dragMoved = new EventEmitter<CdkDragMove<CalendarTaskLayoutModel>>();
  @Output() dragEnded = new EventEmitter<CdkDragEnd>();
  @Output() resizeEnded = new EventEmitter<TaskResizePayload>();

  // Resize gesture state. Non-null while the user is dragging an edge.
  resizing: 'start' | 'end' | null = null;
  previewStart: number | null = null;
  previewDuration: number | null = null;

  private origStart = 0;
  private origDuration = 0;
  private startPointerY = 0;
  private cleanupListeners: (() => void) | null = null;

  // Position/size getters use the resize preview when active so the block
  // grows or shrinks live during the drag without committing yet.
  get topPx(): number {
    return (this.previewStart ?? this.task.startHour) * this.hourHeight;
  }

  get heightPx(): number {
    const dur = this.previewDuration ?? this.task.duration;
    return Math.max(dur * this.hourHeight - 4, 20);
  }

  // Cards shorter than 40 px can't fit the two-row stack without clipping
  // the title or wrapping the time. Below the threshold, the template
  // switches to a single inline row (title + time on the same baseline)
  // and shrinks the type and the completion disc to match. 40 px lands
  // between a 45-min slot (~35 px) and a 60-min slot (~48 px) at the
  // default HOUR_HEIGHT, so it's a natural break.
  get isCompact(): boolean {
    return this.heightPx < 40;
  }

  // Google-Calendar-style cascade-with-overlap. When raised, the card jumps
  // above all others in its conflict group and its right edge extends to
  // the column's right edge (the left edge stays put — see design spec
  // 2026-05-24-calendar-overlapping-events-stacking-design.md).
  get leftStyle(): string {
    return `${this.task._left}%`;
  }

  get widthStyle(): string {
    if (this.raised) return `${100 - this.task._left}%`;
    return `${this.task._width}%`;
  }

  get zIndexStyle(): number {
    if (this.raised) return 999;
    return this.task._zIndex;
  }

  get isInCascade(): boolean {
    return this.task._width < 100;
  }

  // Click on the card body: always open the detail. For cascaded cards
  // that aren't already raised, also raise them so the visual state
  // reflects which card opened. stopPropagation prevents the day-cell's
  // onCellClick from also firing and creating a new event in the slot.
  onBodyClick(event: MouseEvent) {
    event.stopPropagation();
    if (this.isInCascade && !this.raised) {
      this.raiseRequested.emit(this.task);
    }
    this.clicked.emit(this.task);
  }

  // Live time labels — show the preview values during a resize so the user
  // sees the new start/end as the block grows.
  get displayStartText(): string {
    if (this.previewStart != null) return this.formatHour(this.previewStart);
    return this.task.startText;
  }

  get displayEndText(): string {
    if (this.previewStart != null || this.previewDuration != null) {
      const start = this.previewStart ?? this.task.startHour;
      const dur = this.previewDuration ?? this.task.duration;
      return this.formatHour(start + dur);
    }
    return this.task.endText;
  }

  onCompletionClick(event: MouseEvent) {
    event.stopPropagation();
    // Once completed, the indicator becomes inert — uncompletion is not
    // supported (the SDK case status is one-way; mirrors the backend
    // UncompleteNotSupported guard) and a click should be a no-op rather
    // than firing a doomed HTTP round-trip.
    if (this.task.completed) return;
    this.toggleComplete.emit(this.task);
  }

  startResize(ev: MouseEvent, edge: 'start' | 'end') {
    if (this.task.completed) return;
    // Stop propagation so cdkDrag's pointer-down listener (on the same
    // outer element) does not also activate a move-drag.
    ev.stopPropagation();
    ev.preventDefault();

    this.resizing = edge;
    this.origStart = this.task.startHour;
    this.origDuration = this.task.duration;
    this.startPointerY = ev.clientY;
    this.previewStart = this.origStart;
    this.previewDuration = this.origDuration;

    const move = (e: MouseEvent) => this.onResizeMove(e);
    const up = (e: MouseEvent) => this.onResizeEnd(e);
    window.addEventListener('mousemove', move);
    window.addEventListener('mouseup', up);
    this.cleanupListeners = () => {
      window.removeEventListener('mousemove', move);
      window.removeEventListener('mouseup', up);
    };
  }

  private onResizeMove(ev: MouseEvent) {
    if (!this.resizing) return;
    const {newStart, newDuration} = this.computeResize(ev.clientY - this.startPointerY);
    this.previewStart = newStart;
    this.previewDuration = newDuration;
  }

  private onResizeEnd(ev: MouseEvent) {
    if (!this.resizing) return;
    this.cleanupListeners?.();
    this.cleanupListeners = null;

    const {newStart, newDuration} = this.computeResize(ev.clientY - this.startPointerY);
    const edge = this.resizing;
    this.resizing = null;
    this.previewStart = null;
    this.previewDuration = null;

    if (newStart !== this.origStart || newDuration !== this.origDuration) {
      this.resizeEnded.emit({task: this.task, edge, newStartHour: newStart, newDuration});
    }
  }

  // Convert pixel delta → snapped (15-min) new start/duration, clamped to
  // [0, 24] and a 15-min minimum duration.
  private computeResize(deltaPx: number): {newStart: number; newDuration: number} {
    const deltaH = deltaPx / this.hourHeight;
    const snapped = Math.round(deltaH * 4) / 4;
    let newStart = this.origStart;
    let newEnd = this.origStart + this.origDuration;
    const minDur = 0.25;
    if (this.resizing === 'start') {
      newStart = Math.max(0, Math.min(newEnd - minDur, this.origStart + snapped));
    } else {
      newEnd = Math.min(24, Math.max(newStart + minDur, newEnd + snapped));
    }
    return {newStart, newDuration: newEnd - newStart};
  }

  private formatHour(h: number): string {
    const hh = Math.floor(h);
    const mm = Math.round((h % 1) * 60);
    return `${hh.toString().padStart(2, '0')}:${mm.toString().padStart(2, '0')}`;
  }
}
