import {Component, Inject} from '@angular/core';
import {MAT_DIALOG_DATA, MatDialogRef} from '@angular/material/dialog';
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
  ) {}

  onConfirm() {
    // TODO: Board deletion not yet supported by backend
    this.dialogRef.close(null);
  }

  onCancel() {
    this.dialogRef.close(null);
  }
}
