import {Component, Inject, OnInit} from '@angular/core';
import {MAT_DIALOG_DATA, MatDialogRef} from '@angular/material/dialog';
import {CalendarBoardModel} from '../../../../models/calendar';
import {BackendConfigurationPnCalendarService} from '../../../../services';

export interface BoardDeleteModalData {
  board: CalendarBoardModel;
}

@Component({
  standalone: false,
  selector: 'app-board-delete-modal',
  templateUrl: './board-delete-modal.component.html',
})
export class BoardDeleteModalComponent implements OnInit {
  eventCount = 0;
  countLoaded = false;

  constructor(
    private dialogRef: MatDialogRef<BoardDeleteModalComponent>,
    @Inject(MAT_DIALOG_DATA) public data: BoardDeleteModalData,
    private calendarService: BackendConfigurationPnCalendarService,
  ) {}

  ngOnInit() {
    this.calendarService.getBoardEventCount(this.data.board.id).subscribe({
      next: res => {
        if (res && res.success) {
          this.eventCount = res.model;
        }
        this.countLoaded = true;
      },
      error: () => {
        this.countLoaded = true;
      },
    });
  }

  onConfirm() {
    this.calendarService.deleteBoard(this.data.board.id).subscribe(res => {
      if (res && res.success) {
        this.dialogRef.close(true);
      }
    });
  }

  onCancel() {
    this.dialogRef.close(null);
  }
}
