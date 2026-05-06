import {Component, EventEmitter, Inject, OnInit, Optional, Output} from '@angular/core';
import {FormControl, Validators} from '@angular/forms';
import {MAT_DIALOG_DATA, MatDialog, MatDialogRef} from '@angular/material/dialog';
import {Overlay} from '@angular/cdk/overlay';
import {dialogConfigHelper} from 'src/app/common/helpers';
import {CommonDictionaryModel} from 'src/app/common/models';
import {EformVisualEditorModel} from 'src/app/common/models/eforms/visual-editor/eform-visual-editor.model';
import {EformVisualEditorFieldModel} from 'src/app/common/models/eforms/visual-editor/eform-visual-editor-field.model';
import {EformVisualEditorTranslationWithDefaultValue} from 'src/app/common/models/eforms/visual-editor/eform-visual-editor-translation-with-default-value';
import {EformVisualEditorService} from 'src/app/common/services';
import {EformFieldTypesEnum} from 'src/app/common/const/eform-field-types';
import {Store} from '@ngrx/store';
import {selectCurrentUserLanguageId} from 'src/app/state/auth/auth.selector';
import {
  BackendConfigurationPnCalendarFilesService,
  BackendConfigurationPnCalendarService,
  BackendConfigurationPnPropertiesService,
} from '../../../../services';
import {ItemsPlanningPnTagsService} from 'src/app/plugins/modules/items-planning-pn/services/items-planning-pn-tags.service';
import {
  CALENDAR_COLORS,
  CalendarBoardModel,
  CalendarRepeatMeta,
  CalendarTaskAttachment,
  CalendarTaskModel,
  RepeatEditScope,
} from '../../../../models/calendar';
import {CalendarRepeatService, RepeatSelectOption} from '../../services/calendar-repeat.service';
import {computeCopyDate} from '../../services/calendar-copy-date.helper';
import {getCurrentLocale} from '../../services/calendar-locale.helper';
import {CustomRepeatModalComponent} from '../custom-repeat-modal/custom-repeat-modal.component';
import {RepeatScopeModalComponent} from '../repeat-scope-modal/repeat-scope-modal.component';
import {TranslateService} from '@ngx-translate/core';
import {ToastrService} from 'ngx-toastr';
import {of} from 'rxjs';
import {switchMap, take} from 'rxjs/operators';

export interface TaskCreateEditModalData {
  task: CalendarTaskModel | null;
  date: string;
  startHour: number;
  boards: CalendarBoardModel[];
  employees: CommonDictionaryModel[];
  tags: string[];
  propertyId: number;
  properties: CommonDictionaryModel[];
  eforms: {id: number; label: string}[];
  folderId: number | null;
  planningTags: {id: number; name: string}[];
  sourceTask?: CalendarTaskModel | null;  // present in copy mode
}

@Component({
  standalone: false,
  selector: 'app-task-create-edit-modal',
  templateUrl: './task-create-edit-modal.component.html',
  styleUrls: ['./task-create-edit-modal.component.scss'],
})
export class TaskCreateEditModalComponent implements OnInit {
  @Output() popoverClose = new EventEmitter<boolean | null>();
  @Output() timeChanged = new EventEmitter<{startHour: number; endHour: number}>();
  usePopoverMode = false;

  isEditMode = false;
  isReadonly = false;
  repeatOptions: RepeatSelectOption[] = [];
  timeSlots: string[] = [];
  showDriveInput = false;
  filteredBoards: CalendarBoardModel[] = [];
  minDate = new Date();
  selectedTemplate: EformVisualEditorModel | null = null;
  isLoadingTemplate = false;
  showEformDetails = false;
  showMiniPicker = false;
  filteredEmployees: CommonDictionaryModel[] = [];
  private customRepeatMeta: CalendarRepeatMeta | null = null;
  private currentLanguageId = 1;  // default to English

  // Per-AreaRulePlanning file attachments. Mutated from the upload/delete
  // handlers below; rendered as 80x80 thumbnails (PNG/JPEG) or filetype icons
  // (PDF) under the "Vedhæft fil" link.
  attachments: CalendarTaskAttachment[] = [];
  uploadingFiles: { name: string; progress: 'uploading' | 'error'; error?: string }[] = [];

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
  eformControl = new FormControl<number | null>(null);
  planningTagControl = new FormControl<number | null>(null);

  constructor(
    @Optional() private dialogRef: MatDialogRef<TaskCreateEditModalComponent>,
    @Optional() @Inject(MAT_DIALOG_DATA) public data: TaskCreateEditModalData,
    private calendarService: BackendConfigurationPnCalendarService,
    private repeatService: CalendarRepeatService,
    private dialog: MatDialog,
    private overlay: Overlay,
    private translate: TranslateService,
    private eformVisualEditorService: EformVisualEditorService,
    private propertiesService: BackendConfigurationPnPropertiesService,
    private store: Store,
    private toastr: ToastrService,
    private tagsService: ItemsPlanningPnTagsService,
    private filesService: BackendConfigurationPnCalendarFilesService,
  ) {}

  addPlanningTag = (name: string): Promise<{id: number; name: string}> => {
    return this.persistTag(name).then(tag => {
      // Belt-and-braces: ng-select normally writes bindValue to the form
      // control after addTag resolves, but with mtx-select's MatFormField
      // wrapping there's an OnPush/CD corner case where the id never
      // reaches planningTagControl. Set it explicitly.
      this.planningTagControl.setValue(tag.id);
      return {id: tag.id, name: tag.name};
    });
  };

  // For the Set tags multi-select. Returns the tag NAME (string) because
  // that select binds to data.tags (string[]), not to {id, name}. Both
  // backing arrays are kept in sync so creation from either field is
  // visible in the other.
  addTagToList = (name: string): Promise<string> => {
    return this.persistTag(name).then(tag => {
      const current = this.tagsControl.value ?? [];
      if (!current.includes(tag.name)) {
        this.tagsControl.setValue([...current, tag.name]);
      }
      return tag.name;
    });
  };

  private persistTag(name: string): Promise<{id: number; name: string}> {
    return new Promise((resolve, reject) => {
      const trimmed = (name ?? '').trim();
      if (!trimmed) { reject(); return; }
      this.tagsService.createPlanningTag({name: trimmed}).subscribe({
        next: (res) => {
          if (res && res.success && res.model) {
            const newTag = {id: res.model.id, name: res.model.name};
            if (!this.data.planningTags.some(t => t.id === newTag.id)) {
              this.data.planningTags = [...this.data.planningTags, newTag];
            }
            if (!this.data.tags.includes(newTag.name)) {
              this.data.tags = [...this.data.tags, newTag.name];
            }
            resolve(newTag);
          } else {
            this.toastr.error(this.translate.instant('Could not create report headline'));
            reject();
          }
        },
        error: () => {
          this.toastr.error(this.translate.instant('Could not create report headline'));
          reject();
        },
      });
    });
  }

  ngOnInit() {
    this.store.select(selectCurrentUserLanguageId).pipe(take(1)).subscribe(langId => {
      this.currentLanguageId = langId ?? 1;
    });

    this.isEditMode = !!this.data.task;
    this.timeSlots = this.generateTimeSlots();
    this.filteredBoards = this.data.boards;

    const task = this.data.task;
    const sourceTask = this.data.sourceTask;
    const isCopyMode = !task && !!sourceTask;
    let defaultDate = task ? task.taskDate : this.data.date;
    if (isCopyMode) {
      // Adjust past source dates forward to today (or tomorrow if today's
      // start time has already passed).
      defaultDate = computeCopyDate(sourceTask!.taskDate, sourceTask!.startHour);
    }
    const baseDate = new Date(defaultDate);
    this.repeatOptions = this.repeatService.buildRepeatSelectOptions(baseDate, this.customRepeatMeta);

    // Initialize controls
    this.dateControl.setValue(new Date(defaultDate));

    if (task) {
      this.titleControl.setValue(task.title);
      this.startTimeControl.setValue(this.hourToTimeStr(task.startHour));
      this.endTimeControl.setValue(this.hourToTimeStr(task.startHour + task.duration));
      // Reconstruct a CalendarRepeatMeta from the persisted fields so a saved
      // custom rule (incl. multi-day weekly via repeatWeekdaysCsv) lands back
      // on the synthesized 'customCurrent' option with the readable summary,
      // and re-opening the custom modal pre-populates from the existing rule.
      // Reconstruction returns null for legacy rows we can't fully recover —
      // fall through to the unmodified `repeatRule` string in that case.
      const reconstructed = task.repeatRule && task.repeatRule !== 'none'
        ? this.repeatService.reconstructMetaFromTask(task) : null;
      if (reconstructed) {
        this.customRepeatMeta = reconstructed;
        this.repeatOptions = this.repeatService.buildRepeatSelectOptions(baseDate, reconstructed);
        this.repeatControl.setValue('customCurrent', {emitEvent: false});
      } else {
        this.repeatControl.setValue(task.repeatRule ?? 'none');
      }
      this.assigneeControl.setValue(task.assigneeIds ?? []);
      this.tagsControl.setValue(task.tags ?? []);
      this.descriptionControl.setValue(task.descriptionHtml ?? '');
      this.driveLinkControl.setValue(task.driveLink ?? '');
      this.showDriveInput = !!task.driveLink;
      this.boardControl.setValue(task.boardId ?? null);
      this.propertyControl.setValue(task.propertyId ?? this.data.propertyId);
      this.eformControl.setValue(task['eformId'] ?? null);
      this.planningTagControl.setValue(task['itemPlanningTagId'] ?? null);
      // Seed attachments from the task DTO. The backend mapper populates
      // `attachments` for every occurrence of a recurring rule (master-rule
      // scope) — copy mode intentionally does NOT carry attachments forward.
      this.attachments = task.attachments ? [...task.attachments] : [];
    } else if (isCopyMode) {
      const copyPrefix = this.translate.instant('Copy of');
      this.titleControl.setValue(`${copyPrefix} ${sourceTask.title}`);
      this.startTimeControl.setValue(this.hourToTimeStr(sourceTask.startHour));
      this.endTimeControl.setValue(this.hourToTimeStr(sourceTask.startHour + sourceTask.duration));
      // Same reconstruction logic as edit mode — copy carries the source's
      // custom-repeat rule forward so the copied event opens with the same
      // selected option as the original.
      const reconstructed = sourceTask.repeatRule && sourceTask.repeatRule !== 'none'
        ? this.repeatService.reconstructMetaFromTask(sourceTask) : null;
      if (reconstructed) {
        this.customRepeatMeta = reconstructed;
        this.repeatOptions = this.repeatService.buildRepeatSelectOptions(baseDate, reconstructed);
        this.repeatControl.setValue('customCurrent', {emitEvent: false});
      } else {
        this.repeatControl.setValue(sourceTask.repeatRule ?? 'none');
      }
      this.assigneeControl.setValue(sourceTask.assigneeIds ?? []);
      this.tagsControl.setValue(sourceTask.tags ?? []);
      this.descriptionControl.setValue(sourceTask.descriptionHtml ?? '');
      this.driveLinkControl.setValue(sourceTask.driveLink ?? '');
      this.showDriveInput = !!sourceTask.driveLink;
      this.boardControl.setValue(sourceTask.boardId ?? null);
      this.propertyControl.setValue(sourceTask.propertyId ?? this.data.propertyId);
      this.eformControl.setValue(sourceTask['eformId'] ?? null);
      this.planningTagControl.setValue(sourceTask['itemPlanningTagId'] ?? null);
    } else {
      const startHour = this.data.startHour ?? 9;
      this.startTimeControl.setValue(this.hourToTimeStr(startHour));
      this.endTimeControl.setValue(this.hourToTimeStr(startHour + 1));
      this.propertyControl.setValue(this.data.propertyId);
      const defaultBoard = this.data.boards.length > 0
        ? this.data.boards.reduce((min, b) => b.id < min.id ? b : min)
        : null;
      this.boardControl.setValue(defaultBoard?.id ?? null);
      const kvittering = this.data.eforms?.find(e => e.label === 'Kvittering');
      this.eformControl.setValue(kvittering?.id ?? this.data.eforms?.[0]?.id ?? null);
    }

    // Disable all controls for past tasks
    if (this.isEditMode && this.dateControl.value) {
      const taskDate = this.dateControl.value;
      const endTime = this.endTimeControl.value || '00:00';
      if (this.isInPast(taskDate, endTime)) {
        this.isReadonly = true;
        this.titleControl.disable();
        this.dateControl.disable();
        this.startTimeControl.disable();
        this.endTimeControl.disable();
        this.repeatControl.disable();
        this.assigneeControl.disable();
        this.tagsControl.disable();
        this.descriptionControl.disable();
        this.driveLinkControl.disable();
        this.propertyControl.disable();
        this.boardControl.disable();
        this.eformControl.disable();
        this.planningTagControl.disable();
      }
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

    // Emit time changes for selection indicator sizing
    this.startTimeControl.valueChanges.subscribe(() => this.emitTimeChanged());
    this.endTimeControl.valueChanges.subscribe(() => this.emitTimeChanged());

    // When repeat changes, handle custom modal
    this.repeatControl.valueChanges.subscribe(value => {
      if (value === 'custom') {
        this.onRepeatChange();
      }
    });

    // When property changes, reload boards, reload filtered employees, clear stale assignee selections
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
      this.loadEmployeesForProperty(propertyId);
      this.assigneeControl.setValue([]);
    });

    // Load eForm template details when selection changes
    // Use switchMap so rapid eForm switches cancel any in-flight getSingle()
    // and we never overwrite the current selection with a stale response.
    this.eformControl.valueChanges.pipe(
      switchMap(id => {
        if (!id) {
          this.selectedTemplate = null;
          return of(null);
        }
        this.isLoadingTemplate = true;
        return this.eformVisualEditorService.getVisualEditorTemplate(id);
      })
    ).subscribe(res => {
      if (res && res.success && res.model) {
        this.selectedTemplate = res.model;
      }
      this.isLoadingTemplate = false;
    });

    // When date changes, rebuild repeat options and regenerate time slots
    this.dateControl.valueChanges.subscribe(date => {
      if (date) {
        this.repeatOptions = this.repeatService.buildRepeatSelectOptions(date, this.customRepeatMeta);
        this.timeSlots = this.generateTimeSlots();
      }
    });

    // Emit initial time values
    this.emitTimeChanged();

    // Initial data loads
    this.loadEmployeesForProperty(this.propertyControl.value);
    this.loadTemplate(this.eformControl.value);
  }

  loadTemplate(id: number | null) {
    if (!id) {
      this.selectedTemplate = null;
      return;
    }
    this.isLoadingTemplate = true;
    this.eformVisualEditorService.getVisualEditorTemplate(id).subscribe({
      next: res => {
        if (res && res.success && res.model) {
          this.selectedTemplate = res.model;
        }
        this.isLoadingTemplate = false;
      },
      error: () => {
        this.selectedTemplate = null;
        this.isLoadingTemplate = false;
      },
    });
  }

  loadEmployeesForProperty(propertyId: number | null) {
    if (!propertyId) {
      this.filteredEmployees = [];
      return;
    }
    this.propertiesService.getLinkedSites(propertyId, false).subscribe(res => {
      if (res && res.success && res.model) {
        this.filteredEmployees = res.model;
      }
    });
  }

  getTemplateFields(): { type: string; label: string; mandatory: boolean }[] {
    const out: { type: string; label: string; mandatory: boolean }[] = [];
    if (!this.selectedTemplate) return out;
    this.collectFromFields(this.selectedTemplate.fields, out);
    for (const cl of (this.selectedTemplate.checkLists ?? [])) {
      this.collectFromChecklist(cl, out);
    }
    return out;
  }

  private collectFromChecklist(model: EformVisualEditorModel, out: { type: string; label: string; mandatory: boolean }[]): void {
    this.collectFromFields(model.fields, out);
    for (const cl of (model.checkLists ?? [])) {
      this.collectFromChecklist(cl, out);
    }
  }

  private collectFromFields(fields: EformVisualEditorFieldModel[] | null | undefined, out: { type: string; label: string; mandatory: boolean }[]): void {
    for (const f of (fields ?? [])) {
      // SaveButton fields are UI-only submit controls, not user-facing data
      // entry — skip them in the preview.
      if (f.fieldType !== EformFieldTypesEnum.SaveButton) {
        out.push({
          type: this.fieldTypeLabel(f.fieldType),
          label: this.translatedName(f.translations),
          mandatory: !!f.mandatory,
        });
      }
      if (f.fields && f.fields.length > 0) {
        this.collectFromFields(f.fields, out);
      }
    }
  }

  private fieldTypeLabel(t: number): string {
    const name = EformFieldTypesEnum[t];
    if (!name) return '';
    // Translate the raw enum name (e.g. "Picture", "Text"). If no translation
    // exists the pipe falls back to the English name which is still readable.
    return this.translate.instant(name);
  }

  private translatedName(translations: EformVisualEditorTranslationWithDefaultValue[]): string {
    if (!translations || translations.length === 0) return '';
    const match = translations.find(tr => tr.languageId === this.currentLanguageId && !!tr.name);
    if (match) return match.name;
    // Fallback: first non-empty name in any language
    const fallback = translations.find(tr => !!tr.name);
    return fallback ? fallback.name : '';
  }

  toggleEformDetails() {
    this.showEformDetails = !this.showEformDetails;
  }

  get formattedDate(): string {
    const d = this.dateControl.value;
    if (!d) return '';
    return d.toLocaleDateString(getCurrentLocale(this.translate), {weekday: 'long', day: 'numeric', month: 'long'});
  }

  private generateTimeSlots(): string[] {
    const slots: string[] = [];
    const now = new Date();
    const selectedDate = this.dateControl.value;
    const isToday = selectedDate ? selectedDate.toDateString() === now.toDateString() : false;
    for (let h = 0; h < 24; h++) {
      for (let m = 0; m < 60; m += 15) {
        if (isToday && (h < now.getHours() || (h === now.getHours() && m < now.getMinutes()))) {
          continue;
        }
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

  // Accepts: "18:04", "1804", "8:4", "08:04", "804" → all → "18:04"/"08:04".
  // Throws on invalid input so ng-select rejects the entry and keeps the
  // dropdown open — see addTag callback contract.
  addTimeTag = (term: string): string => {
    const trimmed = (term ?? '').trim();
    let normalized: string;
    if (trimmed.includes(':')) {
      const [h, m] = trimmed.split(':');
      if (!h || !m) throw new Error('Invalid time');
      normalized = `${h.padStart(2, '0')}:${m.padStart(2, '0')}`;
    } else if (/^\d{3,4}$/.test(trimmed)) {
      const padded = trimmed.padStart(4, '0');
      normalized = `${padded.slice(0, 2)}:${padded.slice(2)}`;
    } else {
      throw new Error('Invalid time');
    }
    if (!/^([01]\d|2[0-3]):[0-5]\d$/.test(normalized)) {
      throw new Error('Invalid time');
    }
    return normalized;
  };

  autoGrowTextarea(event: Event) {
    const el = event.target as HTMLTextAreaElement;
    el.style.height = 'auto';
    el.style.height = el.scrollHeight + 'px';
  }

  private emitTimeChanged() {
    const startH = this.timeStrToHour(this.startTimeControl.value!);
    const endH = this.timeStrToHour(this.endTimeControl.value!);
    this.timeChanged.emit({startHour: startH, endHour: endH});
  }

  private onRepeatChange() {
    // Capture the meta as it was BEFORE the modal opened so a Cancel-from-edit
    // can restore it instead of wiping a configured rule. Without this snapshot,
    // re-opening the modal to inspect a saved custom rule and pressing Cancel
    // would silently destroy the user's selection.
    const previousMeta = this.customRepeatMeta;
    const ref = this.dialog.open(
      CustomRepeatModalComponent,
      dialogConfigHelper(this.overlay, {
        date: this.dateControl.value ?? new Date(),
        meta: this.customRepeatMeta,  // pre-populate when re-opening with an existing rule
      })
    );
    ref.afterClosed().subscribe((meta: CalendarRepeatMeta | null) => {
      const baseDate = this.dateControl.value ?? new Date();
      if (!meta) {
        // Cancelled. If we had a custom rule before opening, keep it and restore
        // the synthesized 'customCurrent' selection. Otherwise fall back to 'none'.
        if (previousMeta) {
          // Defensive: ensure repeatOptions still contains the synthesized
          // option so the form value resolves to a real entry. (If a date
          // change rebuilt the options between modal open and close, the
          // synthesized option would be missing.)
          this.repeatOptions = this.repeatService.buildRepeatSelectOptions(baseDate, previousMeta);
          this.repeatControl.setValue('customCurrent', {emitEvent: false});
        } else {
          this.repeatControl.setValue('none', {emitEvent: false});
          this.customRepeatMeta = null;
        }
      } else {
        this.customRepeatMeta = meta;
        this.repeatOptions = this.repeatService.buildRepeatSelectOptions(baseDate, meta);
        this.repeatControl.setValue('customCurrent', {emitEvent: false});
      }
    });
  }

  private isInPast(date: Date, timeStr: string): boolean {
    const [hours, minutes] = timeStr.split(':').map(Number);
    const taskDate = new Date(date);
    taskDate.setHours(hours, minutes, 0, 0);
    return taskDate < new Date();
  }

  onSave() {
    if (this.titleControl.invalid) return;
    // Only block past-date save in edit mode. Copy mode may open with a past
    // date seeded from the source event; the user is expected to pick a new
    // date before saving, and we surface that via the standard datepicker
    // min-date validator rather than silently returning.
    if (this.isEditMode && this.isInPast(this.dateControl.value!, this.startTimeControl.value!)) {
      return;
    }

    const startHour = this.timeStrToHour(this.startTimeControl.value!);
    const endHour = this.timeStrToHour(this.endTimeControl.value!);
    const duration = Math.max(endHour - startHour, 0.25);
    const taskDate = this.dateControl.value!;
    const dateStr = `${taskDate.getFullYear()}-${(taskDate.getMonth() + 1).toString().padStart(2, '0')}-${taskDate.getDate().toString().padStart(2, '0')}`;

    const repeatRuleMap: Record<string, number> = {
      'none': 0,
      'daily': 1,
      'weekly': 2, 'weeklyOne': 2, 'weeklyAll': 2,
      'monthly': 3, 'monthlyDom': 3,
      'yearly': 4, 'yearlyOne': 4,
      'weekdays': 5,
      'custom': 6,
      'customCurrent': 6,
    };
    const repeatRuleValue = this.repeatControl.value ?? 'none';

    // For custom repeat, map the meta's kind back to a standard repeatType
    // and use the meta's step as repeatEvery
    let resolvedRepeatType = repeatRuleMap[repeatRuleValue] ?? 0;
    let resolvedRepeatEvery = 1;
    let repeatEndMode = 0; // 0=Never
    let repeatOccurrences: number | null = null;
    let repeatUntilDate: string | null = null;

    const isCustomRule = repeatRuleValue === 'custom' || repeatRuleValue === 'customCurrent';
    if (isCustomRule && this.customRepeatMeta) {
      const meta = this.customRepeatMeta;
      const kindMap: Record<string, number> = {
        'daily': 1, 'everyNd': 1,
        'weeklyOne': 2, 'weeklyMulti': 2, 'everyNWeekOne': 2, 'everyNWeekMulti': 2, 'everyNWeekAll': 2,
        'monthlyDom': 3, 'everyNMonthDom': 3,
        'yearlyOne': 4, 'everyNYear': 4,
      };
      resolvedRepeatType = kindMap[meta.kind] ?? 0;
      resolvedRepeatEvery = meta.n ?? 1;

      if (meta.endMode === 'after' && meta.afterCount) {
        repeatEndMode = 1;
        repeatOccurrences = meta.afterCount;
      } else if (meta.endMode === 'until' && meta.untilTs) {
        repeatEndMode = 2;
        repeatUntilDate = new Date(meta.untilTs).toISOString();
      }
    }

    const payload: any = {
      // Backend CalendarTaskCreateRequestModel fields
      translates: [{name: this.titleControl.value, languageId: 1}],
      startDate: taskDate,
      startHour,
      duration,
      sites: this.assigneeControl.value ?? [],
      tagIds: (this.tagsControl.value ?? []).map((t: any) => {
        if (typeof t === 'number') return t;
        const match = this.data.planningTags.find(pt => pt.name === t);
        return match?.id ?? 0;
      }).filter((id: number) => id > 0),
      boardId: this.boardControl.value,
      color: this.filteredBoards.find(b => b.id === this.boardControl.value)?.color ?? CALENDAR_COLORS[0],
      descriptionHtml: this.descriptionControl.value ?? '',
      repeatType: resolvedRepeatType,
      repeatEvery: resolvedRepeatEvery,
      repeatEndMode,
      repeatOccurrences,
      repeatUntilDate,
      // CSV of JS getDay() weekday indices for multi-day weekly custom rules.
      // Sent as null for any non-custom rule (isCustomRule=false), which
      // unconditionally clears any stale CSV the row may carry from a prior
      // custom selection. See spec — Layer 3 / "explicit clearing rule".
      repeatWeekdaysCsv: (isCustomRule && this.customRepeatMeta?.weekdays?.length)
        ? this.customRepeatMeta.weekdays.join(',')
        : null,
      driveLink: this.driveLinkControl.value ?? '',
      propertyId: this.propertyControl.value ?? this.data.propertyId,
      status: 1,
      complianceEnabled: true,
      folderId: this.data.folderId,
      eformId: this.eformControl.value,
      itemPlanningTagId: this.planningTagControl.value,

      // Keep these for local/UI use and backward compat
      title: this.titleControl.value,
      taskDate: dateStr,
      startText: this.startTimeControl.value,
      endText: this.endTimeControl.value,
      assigneeIds: this.assigneeControl.value ?? [],
      tags: this.tagsControl.value ?? [],
      repeatRule: repeatRuleValue === 'customCurrent' ? 'custom' : repeatRuleValue,
      id: this.data.task?.id,
      repeatSeriesId: this.data.task?.repeatSeriesId,
    };

    const doSave = (scope?: string) => {
      const obs = this.isEditMode
        ? this.calendarService.updateTask(payload, (scope ?? 'this') as RepeatEditScope)
        : this.calendarService.createTask(payload);

      obs.subscribe({
        next: res => {
          if (res && res.success) {
            this.close(true);
          } else {
            const msg = (res && res.message)
              ? res.message
              : this.translate.instant('Could not save the event');
            this.toastr.error(msg, this.translate.instant('Error'));
          }
        },
        error: err => {
          const msg = err?.error?.message || err?.message || this.translate.instant('Could not save the event');
          this.toastr.error(msg, this.translate.instant('Error'));
        },
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

  get formattedSelectedDate(): string {
    const value = this.dateControl.value;
    if (!value) return '';
    const locale = getCurrentLocale(this.translate);
    const formatted = value.toLocaleDateString(locale, {weekday: 'long', day: 'numeric', month: 'long'});
    return formatted.charAt(0).toUpperCase() + formatted.slice(1);
  }

  onMiniDateSelected(date: Date) {
    this.dateControl.setValue(date);
    this.showMiniPicker = false;
  }

  private close(result: boolean | null) {
    if (this.usePopoverMode) {
      this.popoverClose.emit(result);
    } else {
      this.dialogRef.close(result);
    }
  }

  // ---- File attachments ---------------------------------------------------

  /** Hidden <input type="file" #fileInput> change handler. */
  onFileInputChange(event: Event): void {
    const input = event.target as HTMLInputElement;
    if (!input.files || input.files.length === 0) return;
    this.uploadFiles(input.files);
    // Reset so re-selecting the same file fires `change` again. Without this,
    // a delete-then-reupload-same-name flow would no-op silently.
    input.value = '';
  }

  /**
   * Upload files sequentially. The backend enforces the 10-file quota per
   * AreaRulePlanning; sequential POSTs avoid a race between two uploads
   * both seeing `count == 9` and slipping past the quota check.
   *
   * On failure the chip is mutated to `progress: 'error'` and left in
   * `uploadingFiles` so the user sees what went wrong (auto-dismissed after
   * 4 s). Removing it on error would silently lose the failure signal.
   * Successful chips are filtered out immediately — the new attachment row
   * replaces the chip in the UI.
   *
   * Error toasts come from `ApiBaseService.extractData` for `success: false`
   * envelopes; we only fire our own toast for transport-level errors.
   */
  uploadFiles(files: FileList): void {
    const taskId = this.data.task?.id;
    if (!taskId) return;  // create-mode is gated in the template

    const queue: File[] = [];
    for (let i = 0; i < files.length; i++) queue.push(files.item(i)!);

    const next = () => {
      const file = queue.shift();
      if (!file) return;
      const chip: { name: string; progress: 'uploading' | 'error'; error?: string } = {
        name: file.name, progress: 'uploading',
      };
      this.uploadingFiles = [...this.uploadingFiles, chip];

      this.filesService.uploadFile(taskId, file).subscribe({
        next: res => {
          if (res && res.success && res.model) {
            this.uploadingFiles = this.uploadingFiles.filter(u => u !== chip);
            this.attachments = [...this.attachments, res.model];
          } else {
            // Server-side reject (success: false). ApiBaseService.extractData
            // already fired a toast for res.message — don't duplicate it.
            // Mutate the chip in-place so the *ngIf="u.progress === 'error'"
            // branch in the template renders, then auto-fade after 4 s.
            chip.progress = 'error';
            chip.error = (res && res.message) ? res.message : this.translate.instant('Error');
            this.uploadingFiles = [...this.uploadingFiles];
            setTimeout(() => {
              this.uploadingFiles = this.uploadingFiles.filter(u => u !== chip);
            }, 4000);
          }
          next();
        },
        error: err => {
          // Transport-level failure — extractData never ran. Toast here is
          // the only signal the user gets, alongside the persisted chip.
          const msg = err?.error?.message || err?.message || this.translate.instant('Error');
          chip.progress = 'error';
          chip.error = msg;
          this.uploadingFiles = [...this.uploadingFiles];
          this.toastr.error(msg, this.translate.instant('Error'));
          setTimeout(() => {
            this.uploadingFiles = this.uploadingFiles.filter(u => u !== chip);
          }, 4000);
          next();
        },
      });
    };
    next();
  }

  /**
   * Opens the attachment in a new tab. Plain `window.open(url)` would 401:
   * the new-tab GET has no bearer token. Fetch as a Blob (auth header set
   * by `ApiBaseService.getBlobData`), then open the resulting object URL.
   * The blob URL is intentionally NOT revoked — `URL.revokeObjectURL` while
   * the new tab is still loading would break the preview.
   */
  downloadAttachment(att: CalendarTaskAttachment): void {
    const taskId = this.data.task?.id;
    if (!taskId) return;
    this.filesService.getFileBlob(taskId, att.id).subscribe({
      next: (blob: Blob) => {
        const url = URL.createObjectURL(blob);
        window.open(url, '_blank');
      },
      error: () => this.toastr.error(this.translate.instant('Could not download file')),
    });
  }

  deleteAttachment(att: CalendarTaskAttachment): void {
    const taskId = this.data.task?.id;
    if (!taskId) return;
    const confirmMsg = this.translate.instant('Delete attachment?');
    if (!window.confirm(confirmMsg)) return;
    this.filesService.deleteFile(taskId, att.id).subscribe({
      next: res => {
        if (res && res.success) {
          this.attachments = this.attachments.filter(a => a.id !== att.id);
        }
        // Server-side reject (success: false): ApiBaseService.extractData
        // already toasted res.message; nothing to do here.
      },
      error: err => {
        // Transport-level failure — toast it explicitly.
        const msg = err?.error?.message || err?.message || this.translate.instant('Error');
        this.toastr.error(msg, this.translate.instant('Error'));
      },
    });
  }

  isImage(att: CalendarTaskAttachment): boolean {
    // Match the server-side whitelist (see calendar-files endpoint) exactly
    // rather than `image/*` — a hypothetical image/svg+xml or image/webp slipping
    // past would render here but never have been accepted by upload.
    return att.mimeType === 'image/png' || att.mimeType === 'image/jpeg';
  }

  formatSize(bytes: number): string {
    if (bytes == null || bytes < 0) return '';
    if (bytes < 1024) return `${bytes} B`;
    const kb = bytes / 1024;
    if (kb < 1024) return `${Math.round(kb)} KB`;
    const mb = kb / 1024;
    return `${(Math.round(mb * 10) / 10).toFixed(1)} MB`;
  }
}
