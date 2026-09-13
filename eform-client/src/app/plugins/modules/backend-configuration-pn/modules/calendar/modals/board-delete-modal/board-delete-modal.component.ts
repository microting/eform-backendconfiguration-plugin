import {Component, Inject, OnInit} from '@angular/core';
import {MAT_DIALOG_DATA, MatDialogRef} from '@angular/material/dialog';
import {CalendarBoardModel} from '../../../../models/calendar';
import {BackendConfigurationPnCalendarService} from '../../../../services';

export interface BoardDeleteModalData {
  board: CalendarBoardModel;
  /**
   * How many calendars the property has, including this one. Only used to warn
   * that deleting the last one is not the same as having none — see
   * `isLastBoard`.
   */
  boardCount: number;
}

/**
 * Delete-calendar confirmation (#1210).
 *
 * Two things here are deliberate and easy to "fix" wrongly:
 *
 * 1. **Nothing is removed optimistically.** `DeleteBoard` cascades
 *    `DeleteEntireSeries` over every series on the board and aborts, leaving
 *    the board intact, if any one of them fails. So the dialog only closes with
 *    a truthy result once the server says it succeeded; on failure it stays
 *    open, re-arms the button and shows the error.
 * 2. **The count is awaited before Delete is enabled.** Confirming a
 *    destructive action without the number it is meant to inform you of is
 *    worse than a short wait.
 */
@Component({
  standalone: false,
  selector: 'app-board-delete-modal',
  templateUrl: './board-delete-modal.component.html',
  styles: [`
    .board-delete-hint {
      color: var(--text-muted, rgba(0, 0, 0, 0.6));
      font-size: 13px;
    }
  `],
})
export class BoardDeleteModalComponent implements OnInit {
  eventCount = 0;
  countLoaded = false;
  deleting = false;
  failed = false;

  constructor(
    private dialogRef: MatDialogRef<BoardDeleteModalComponent>,
    @Inject(MAT_DIALOG_DATA) public data: BoardDeleteModalData,
    private calendarService: BackendConfigurationPnCalendarService,
  ) {}

  /**
   * `GetBoards` auto-creates a "Default" calendar for a property that has none,
   * so deleting the last one does not leave the property empty — it mints a
   * fresh Default on the very next load. Saying so beats letting the user
   * discover it. (#1210 explicitly leaves the auto-create alone.)
   */
  get isLastBoard(): boolean {
    return this.data.boardCount <= 1;
  }

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
    if (this.deleting) {
      return;
    }
    this.deleting = true;
    this.failed = false;
    this.calendarService.deleteBoard(this.data.board.id).subscribe({
      next: res => {
        if (res && res.success) {
          this.dialogRef.close(true);
          return;
        }
        // The cascade aborted: the board still exists. Keep the row, say so.
        this.deleting = false;
        this.failed = true;
      },
      error: () => {
        this.deleting = false;
        this.failed = true;
      },
    });
  }

  onCancel() {
    this.dialogRef.close(null);
  }
}
