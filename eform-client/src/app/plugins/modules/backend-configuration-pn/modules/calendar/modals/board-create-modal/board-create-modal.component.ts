import {Component, Inject, OnInit} from '@angular/core';
import {FormBuilder, FormGroup, Validators} from '@angular/forms';
import {MAT_DIALOG_DATA, MatDialogRef} from '@angular/material/dialog';
import {CALENDAR_COLORS} from '../../../../models/calendar';
import {BackendConfigurationPnCalendarService} from '../../../../services';

export interface BoardCreateModalData {
  propertyId: number;
}

@Component({
  standalone: false,
  selector: 'app-board-create-modal',
  templateUrl: './board-create-modal.component.html',
  styles: [`
    .field-label {
      font-size: 13px;
      color: #5f6368;
      margin-bottom: 4px;
      display: block;
    }
    .color-picker-grid {
      display: grid;
      grid-template-columns: repeat(5, 32px);
      gap: 8px;
      margin-top: 4px;
    }
    .color-swatch {
      width: 32px;
      height: 32px;
      border-radius: 50%;
      cursor: pointer;
      transition: transform 0.15s;
    }
    .color-swatch:hover {
      transform: scale(1.15);
    }
    .color-swatch.selected {
      outline: 2px solid #333;
      outline-offset: 2px;
    }
  `],
})
export class BoardCreateModalComponent implements OnInit {
  form!: FormGroup;
  colors = CALENDAR_COLORS;

  constructor(
    private fb: FormBuilder,
    private dialogRef: MatDialogRef<BoardCreateModalComponent>,
    @Inject(MAT_DIALOG_DATA) public data: BoardCreateModalData,
    private calendarService: BackendConfigurationPnCalendarService,
  ) {}

  ngOnInit() {
    this.form = this.fb.group({
      name: ['', Validators.required],
      color: [CALENDAR_COLORS[0]],
    });
  }

  onSave() {
    if (this.form.invalid) return;
    const {name, color} = this.form.value;
    this.calendarService.createBoard({name, color, propertyId: this.data.propertyId}).subscribe(res => {
      if (res && res.success) {
        this.dialogRef.close(true);
      }
    });
  }

  onCancel() {
    this.dialogRef.close(null);
  }
}
