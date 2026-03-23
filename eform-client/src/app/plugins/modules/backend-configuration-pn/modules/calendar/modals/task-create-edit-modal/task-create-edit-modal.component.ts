import {Component, Inject, OnInit} from '@angular/core';
import {FormBuilder, FormGroup, Validators} from '@angular/forms';
import {MAT_DIALOG_DATA, MatDialog, MatDialogRef} from '@angular/material/dialog';
import {Overlay} from '@angular/cdk/overlay';
import {dialogConfigHelper} from 'src/app/common/helpers';
import {CommonDictionaryModel} from 'src/app/common/models';
import {BackendConfigurationPnCalendarService} from '../../../../services';
import {CALENDAR_COLORS, CalendarBoardModel, CalendarTaskModel} from '../../../../models/calendar';
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
}

@Component({
  standalone: false,
  selector: 'app-task-create-edit-modal',
  templateUrl: './task-create-edit-modal.component.html',
})
export class TaskCreateEditModalComponent implements OnInit {
  form!: FormGroup;
  repeatOptions: RepeatSelectOption[] = [];
  colors = CALENDAR_COLORS;
  isEditMode = false;

  constructor(
    private fb: FormBuilder,
    private dialogRef: MatDialogRef<TaskCreateEditModalComponent>,
    @Inject(MAT_DIALOG_DATA) public data: TaskCreateEditModalData,
    private calendarService: BackendConfigurationPnCalendarService,
    private repeatService: CalendarRepeatService,
    private dialog: MatDialog,
    private overlay: Overlay,
  ) {}

  ngOnInit() {
    this.isEditMode = !!this.data.task;
    const task = this.data.task;

    const defaultDate = task ? task.taskDate : this.data.date;
    const baseDate = new Date(defaultDate);
    this.repeatOptions = this.repeatService.buildRepeatSelectOptions(baseDate);

    this.form = this.fb.group({
      title: [task?.title ?? '', Validators.required],
      taskDate: [defaultDate, Validators.required],
      startHour: [task?.startHour ?? this.data.startHour, Validators.required],
      dur: [task?.dur ?? 1, [Validators.required, Validators.min(0.25)]],
      boardId: [task?.boardId ?? this.data.boards[0]?.id ?? null],
      color: [task?.color ?? CALENDAR_COLORS[0]],
      assigneeIds: [task?.assigneeIds ?? []],
      tags: [task?.tags ?? []],
      descriptionHtml: [task?.descriptionHtml ?? ''],
      repeatRule: [task?.repeatRule ?? 'none'],
      driveLink: [task?.driveLink ?? ''],
    });
    this.form.get('repeatRule')!.valueChanges.subscribe(value => {
      this.onRepeatChange(value);
    });
  }

  onRepeatChange(value: string) {
    if (value === 'custom') {
      const ref = this.dialog.open(
        CustomRepeatModalComponent,
        dialogConfigHelper(this.overlay, {date: new Date(this.form.value.taskDate)})
      );
      ref.afterClosed().subscribe(meta => {
        if (!meta) {
          this.form.patchValue({repeatRule: 'none'});
        }
      });
    }
  }

  onSave() {
    if (this.form.invalid) return;

    const val = this.form.value;
    const startHour: number = val.startHour;
    const endHour = startHour + val.dur;
    const fmt = (h: number) => {
      const hours = Math.floor(h);
      const mins = Math.round((h % 1) * 60);
      return `${hours.toString().padStart(2,'0')}:${mins.toString().padStart(2,'0')}`;
    };

    const doSave = (scope?: string) => {
      const payload = {
        ...val,
        startText: fmt(startHour),
        endText: fmt(endHour),
        propertyId: this.data.propertyId,
        id: this.data.task?.id,
        repeatSeriesId: this.data.task?.repeatSeriesId,
      };

      const obs = this.isEditMode
        ? this.calendarService.updateTask(payload as any, (scope as any) ?? 'this')
        : this.calendarService.createTask(payload as any);

      obs.subscribe(res => {
        if (res && res.success) this.dialogRef.close(true);
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
    this.dialogRef.close(null);
  }
}
