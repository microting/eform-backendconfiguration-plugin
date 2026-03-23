import {Component, EventEmitter, Inject, OnInit, Optional, Output} from '@angular/core';
import {FormControl, Validators} from '@angular/forms';
import {MAT_DIALOG_DATA, MatDialog, MatDialogRef} from '@angular/material/dialog';
import {Overlay} from '@angular/cdk/overlay';
import {dialogConfigHelper} from 'src/app/common/helpers';
import {CommonDictionaryModel} from 'src/app/common/models';
import {BackendConfigurationPnCalendarService} from '../../../../services';
import {CALENDAR_COLORS, CalendarBoardModel, CalendarTaskModel, RepeatEditScope} from '../../../../models/calendar';
import {CalendarRepeatService, RepeatSelectOption} from '../../services/calendar-repeat.service';
import {CustomRepeatModalComponent} from '../custom-repeat-modal/custom-repeat-modal.component';
import {RepeatScopeModalComponent} from '../repeat-scope-modal/repeat-scope-modal.component';

export interface TaskCreateEditModalData {
  task: CalendarTaskModel | null;
  date: string;
  startHour: number;
  boards: CalendarBoardModel[];
  employees: CommonDictionaryModel[];
  tags: string[];
  propertyId: number;
  properties: CommonDictionaryModel[];
}

@Component({
  standalone: false,
  selector: 'app-task-create-edit-modal',
  templateUrl: './task-create-edit-modal.component.html',
  styleUrls: ['./task-create-edit-modal.component.scss'],
})
export class TaskCreateEditModalComponent implements OnInit {
  @Output() popoverClose = new EventEmitter<boolean | null>();
  usePopoverMode = false;

  isEditMode = false;
  repeatOptions: RepeatSelectOption[] = [];
  timeSlots: string[] = [];
  showDriveInput = false;
  filteredBoards: CalendarBoardModel[] = [];

  // Individual form controls
  titleControl = new FormControl('', Validators.required);
  dateControl = new FormControl<Date | null>(null);
  startTimeControl = new FormControl('09:00');
  endTimeControl = new FormControl('10:00');
  repeatControl = new FormControl('none');
  assigneeControl = new FormControl<number[]>([]);
  tagsControl = new FormControl<string[]>([]);
  descriptionControl = new FormControl('');
  driveLinkControl = new FormControl('');
  propertyControl = new FormControl<number | null>(null);
  boardControl = new FormControl<number | null>(null);

  constructor(
    @Optional() private dialogRef: MatDialogRef<TaskCreateEditModalComponent>,
    @Optional() @Inject(MAT_DIALOG_DATA) public data: TaskCreateEditModalData,
    private calendarService: BackendConfigurationPnCalendarService,
    private repeatService: CalendarRepeatService,
    private dialog: MatDialog,
    private overlay: Overlay,
  ) {}

  ngOnInit() {
    this.isEditMode = !!this.data.task;
    this.timeSlots = this.generateTimeSlots();
    this.filteredBoards = this.data.boards;

    const task = this.data.task;
    const defaultDate = task ? task.taskDate : this.data.date;
    const baseDate = new Date(defaultDate);
    this.repeatOptions = this.repeatService.buildRepeatSelectOptions(baseDate);

    // Initialize controls
    this.dateControl.setValue(new Date(defaultDate));

    if (task) {
      this.titleControl.setValue(task.title);
      this.startTimeControl.setValue(this.hourToTimeStr(task.startHour));
      this.endTimeControl.setValue(this.hourToTimeStr(task.startHour + task.dur));
      this.repeatControl.setValue(task.repeatRule ?? 'none');
      this.assigneeControl.setValue(task.assigneeIds ?? []);
      this.tagsControl.setValue(task.tags ?? []);
      this.descriptionControl.setValue(task.descriptionHtml ?? '');
      this.driveLinkControl.setValue(task.driveLink ?? '');
      this.showDriveInput = !!task.driveLink;
      this.boardControl.setValue(task.boardId ?? null);
      this.propertyControl.setValue(task.propertyId ?? this.data.propertyId);
    } else {
      const startHour = this.data.startHour ?? 9;
      this.startTimeControl.setValue(this.hourToTimeStr(startHour));
      this.endTimeControl.setValue(this.hourToTimeStr(startHour + 1));
      this.propertyControl.setValue(this.data.propertyId);
      this.boardControl.setValue(this.data.boards[0]?.id ?? null);
    }

    // When start time changes, auto-adjust end time to maintain duration
    let prevStartH = this.timeStrToHour(this.startTimeControl.value!);
    this.startTimeControl.valueChanges.subscribe(newStart => {
      if (!newStart) return;
      const newStartH = this.timeStrToHour(newStart);
      const endH = this.timeStrToHour(this.endTimeControl.value!);
      const dur = endH - prevStartH;
      const newEnd = Math.min(newStartH + Math.max(dur, 0.25), 24);
      this.endTimeControl.setValue(this.hourToTimeStr(newEnd), {emitEvent: false});
      prevStartH = newStartH;
    });

    // When repeat changes, handle custom modal
    this.repeatControl.valueChanges.subscribe(value => {
      if (value === 'custom') {
        this.onRepeatChange();
      }
    });

    // When property changes, reload boards
    this.propertyControl.valueChanges.subscribe(propertyId => {
      if (propertyId) {
        this.calendarService.getBoards(propertyId).subscribe(res => {
          if (res && res.success) {
            this.filteredBoards = res.model;
            if (this.filteredBoards.length > 0 && !this.filteredBoards.find(b => b.id === this.boardControl.value)) {
              this.boardControl.setValue(this.filteredBoards[0].id);
            }
          }
        });
      }
    });

    // When date changes, rebuild repeat options
    this.dateControl.valueChanges.subscribe(date => {
      if (date) {
        this.repeatOptions = this.repeatService.buildRepeatSelectOptions(date);
      }
    });
  }

  get formattedDate(): string {
    const d = this.dateControl.value;
    if (!d) return '';
    return d.toLocaleDateString('da-DK', {weekday: 'long', day: 'numeric', month: 'long'});
  }

  private generateTimeSlots(): string[] {
    const slots: string[] = [];
    for (let h = 0; h < 24; h++) {
      for (let m = 0; m < 60; m += 15) {
        slots.push(`${h.toString().padStart(2, '0')}:${m.toString().padStart(2, '0')}`);
      }
    }
    return slots;
  }

  private hourToTimeStr(hour: number): string {
    const h = Math.floor(Math.min(hour, 23.75));
    const m = Math.round((hour % 1) * 60);
    return `${h.toString().padStart(2, '0')}:${m.toString().padStart(2, '0')}`;
  }

  private timeStrToHour(time: string): number {
    const parts = time.split(':');
    if (parts.length !== 2) return 0;
    return parseInt(parts[0], 10) + parseInt(parts[1], 10) / 60;
  }

  autoGrowTextarea(event: Event) {
    const el = event.target as HTMLTextAreaElement;
    el.style.height = 'auto';
    el.style.height = el.scrollHeight + 'px';
  }

  private onRepeatChange() {
    const ref = this.dialog.open(
      CustomRepeatModalComponent,
      dialogConfigHelper(this.overlay, {date: this.dateControl.value ?? new Date()})
    );
    ref.afterClosed().subscribe(meta => {
      if (!meta) {
        this.repeatControl.setValue('none', {emitEvent: false});
      }
    });
  }

  onSave() {
    if (this.titleControl.invalid) return;

    const startHour = this.timeStrToHour(this.startTimeControl.value!);
    const endHour = this.timeStrToHour(this.endTimeControl.value!);
    const dur = Math.max(endHour - startHour, 0.25);
    const taskDate = this.dateControl.value!;
    const dateStr = `${taskDate.getFullYear()}-${(taskDate.getMonth() + 1).toString().padStart(2, '0')}-${taskDate.getDate().toString().padStart(2, '0')}`;

    const payload: any = {
      title: this.titleControl.value,
      taskDate: dateStr,
      startHour,
      dur,
      startText: this.startTimeControl.value,
      endText: this.endTimeControl.value,
      boardId: this.boardControl.value,
      color: this.filteredBoards.find(b => b.id === this.boardControl.value)?.color ?? CALENDAR_COLORS[0],
      assigneeIds: this.assigneeControl.value ?? [],
      tags: this.tagsControl.value ?? [],
      descriptionHtml: this.descriptionControl.value ?? '',
      repeatRule: this.repeatControl.value ?? 'none',
      driveLink: this.driveLinkControl.value ?? '',
      propertyId: this.propertyControl.value ?? this.data.propertyId,
      id: this.data.task?.id,
      repeatSeriesId: this.data.task?.repeatSeriesId,
    };

    const doSave = (scope?: string) => {
      const obs = this.isEditMode
        ? this.calendarService.updateTask(payload, (scope ?? 'this') as RepeatEditScope)
        : this.calendarService.createTask(payload);

      obs.subscribe(res => {
        if (res && res.success) this.close(true);
      });
    };

    if (this.isEditMode && this.data.task?.repeatSeriesId) {
      const ref = this.dialog.open(
        RepeatScopeModalComponent,
        dialogConfigHelper(this.overlay, {mode: 'edit'})
      );
      ref.afterClosed().subscribe(scope => {
        if (scope) doSave(scope);
      });
    } else {
      doSave();
    }
  }

  onCancel() {
    this.close(null);
  }

  private close(result: boolean | null) {
    if (this.usePopoverMode) {
      this.popoverClose.emit(result);
    } else {
      this.dialogRef.close(result);
    }
  }
}
