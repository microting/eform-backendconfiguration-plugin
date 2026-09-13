import {Component, Inject, OnInit} from '@angular/core';
import {AbstractControl, FormBuilder, FormGroup, ValidationErrors, ValidatorFn, Validators} from '@angular/forms';
import {MAT_DIALOG_DATA, MatDialogRef} from '@angular/material/dialog';
import {CalendarBoardModel, CALENDAR_COLORS} from '../../../../models/calendar';
import {BackendConfigurationPnCalendarService} from '../../../../services';
import {isDuplicateBoardName} from '../../services/calendar-board-name.helper';

/**
 * `Validators.required` accepts "   " — it only rejects null/undefined/''. The
 * name is trimmed before it is sent, so a whitespace-only name would POST an
 * EMPTY calendar name and the guard would never see it. Reported as `required`
 * so the field shows the same state a genuinely empty one does.
 */
function nonBlankName(control: AbstractControl): ValidationErrors | null {
  return typeof control.value === 'string' && control.value.trim().length === 0
    ? {required: true}
    : null;
}

export interface BoardCreateEditModalData {
  propertyId: number;
  /** Absent = create. Present = edit that calendar. */
  board?: CalendarBoardModel;
  /**
   * Every calendar of the property, for the duplicate-name guard. The dialog
   * cannot fetch this itself: `GET boards/{propertyId}` auto-creates a Default
   * board when the property has none, so an extra load here would have a side
   * effect. The opener already holds the list it rendered the dropdown from.
   */
  boards: CalendarBoardModel[];
}

/**
 * Create / edit a calendar (#1210). Replaces the inline rename popover that
 * lived in the calendars dropdown, and adds the client-side duplicate-name
 * guard — see `calendar-board-name.helper.ts` for why that guard is UI-level.
 */
@Component({
  standalone: false,
  selector: 'app-board-create-edit-modal',
  templateUrl: './board-create-edit-modal.component.html',
  styles: [`
    .field-label {
      font-size: 13px;
      color: #5f6368;
      margin-bottom: 4px;
      display: block;
    }
    /* The duplicate-name message (see the template for why it is not a
       <mat-error>). --text-error is theme-mixin-defined, so it is red under
       theme-eform and theme-workspace alike, light and dark. No negative
       margin to claw back the subscript row: both themes already collapse an
       empty subscript wrapper to display:none (styles.scss:1283 /
       _workspace-mat-overrides.scss:384), and with no <mat-error> left inside
       the field it is always empty, so the message already sits directly
       under the input. */
    .field-error {
      display: block;
      margin: 0 0 4px;
      font-size: 12px;
      line-height: 16px;
      color: var(--text-error, #DB0D0D);
    }
    .color-picker-grid {
      display: grid;
      grid-template-columns: repeat(8, 32px);
      gap: 8px;
      margin-top: 4px;
      min-width: 352px;
    }
    .color-swatch {
      width: 32px;
      height: 32px;
      border-radius: 50%;
      cursor: pointer;
      transition: transform 0.15s;
      border: none;
      padding: 0;
    }
    .color-swatch:hover {
      transform: scale(1.15);
    }
    .color-swatch.selected {
      outline: 2px solid var(--text-header, #333);
      outline-offset: 2px;
    }
  `],
})
export class BoardCreateEditModalComponent implements OnInit {
  form!: FormGroup;
  /** The shipped palette. The picker writes these exact values — #1210 keeps it. */
  colors = CALENDAR_COLORS;
  saving = false;

  constructor(
    private fb: FormBuilder,
    private dialogRef: MatDialogRef<BoardCreateEditModalComponent>,
    @Inject(MAT_DIALOG_DATA) public data: BoardCreateEditModalData,
    private calendarService: BackendConfigurationPnCalendarService,
  ) {}

  get isEdit(): boolean {
    return !!this.data.board;
  }

  get titleKey(): string {
    return this.isEdit ? 'Edit calendar' : 'Create calendar';
  }

  get submitKey(): string {
    return this.isEdit ? 'Save' : 'Create';
  }

  /** True once the user has produced a name that another calendar already owns. */
  get duplicateName(): boolean {
    return !!this.form?.get('name')?.hasError('duplicateBoardName');
  }

  ngOnInit() {
    this.form = this.fb.group({
      name: [
        this.data.board?.name ?? '',
        [Validators.required, nonBlankName, this.uniqueNameValidator()],
      ],
      color: [this.data.board?.color ?? CALENDAR_COLORS[0]],
    });
  }

  selectColor(color: string) {
    this.form.patchValue({color});
  }

  onSave() {
    // `saving` is not only cosmetic: without it a double-click on Create posts
    // two calendars, and the duplicate-name guard cannot catch that because the
    // first one is not in `data.boards` yet.
    if (this.form.invalid || this.saving) {
      return;
    }
    this.saving = true;
    const name = (this.form.value.name as string).trim();
    const color = this.form.value.color as string;

    const request$ = this.isEdit
      ? this.calendarService.updateBoard({id: this.data.board!.id, name, color})
      : this.calendarService.createBoard({name, color, propertyId: this.data.propertyId});

    request$.subscribe({
      next: res => {
        if (res && res.success) {
          this.dialogRef.close(true);
          return;
        }
        // The service already toasted the server's message; re-arm the button
        // so the user can correct and retry instead of being stuck.
        this.saving = false;
      },
      error: () => {
        this.saving = false;
      },
    });
  }

  onCancel() {
    this.dialogRef.close(null);
  }

  /**
   * Case-insensitive uniqueness within the property, ignoring the calendar
   * being edited so an unchanged name is not flagged against itself.
   */
  private uniqueNameValidator(): ValidatorFn {
    return (control: AbstractControl): ValidationErrors | null =>
      isDuplicateBoardName(control.value, this.data.boards ?? [], this.data.board?.id ?? null)
        ? {duplicateBoardName: true}
        : null;
  }
}
