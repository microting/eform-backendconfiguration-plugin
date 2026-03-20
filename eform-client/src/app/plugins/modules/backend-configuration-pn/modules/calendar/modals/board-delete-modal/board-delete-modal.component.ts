import {Component, Inject} from '@angular/core';
import {MAT_DIALOG_DATA, MatDialogRef} from '@angular/material/dialog';
import {BackendConfigurationPnCalendarService} from '../../../../services';
import {CalendarBoardModel} from '../../../../models/calendar';

export interface BoardDeleteModalData {
  board: CalendarBoardModel;
}

@Component({
  standalone: false,
  selector: 'app-board-delete-modal',
  templateUrl: './board-delete-modal.component.html',
})
export class BoardDeleteModalComponent {
  constructor(
    private dialogRef: MatDialogRef<BoardDeleteModalComponent>,
    @Inject(MAT_DIALOG_DATA) public data: BoardDeleteModalData,
    private calendarService: BackendConfigurationPnCalendarService,
  ) {}

  onConfirm() {
    this.calendarService.deleteBoard(this.data.board.id).subscribe(res => {
      if (res && res.success) this.dialogRef.close(true);
    });
  }

  onCancel() {
    this.dialogRef.close(null);
  }
}
