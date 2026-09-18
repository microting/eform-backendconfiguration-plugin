import {Component, Inject} from '@angular/core';
import {MAT_DIALOG_DATA, MatDialogRef} from '@angular/material/dialog';
import {CalendarBoardModel} from '../../../../../models/calendar';
import {BackendConfigurationPnTaskListService} from '../../../../../services';
import {TaskListBatchModalData} from '../../task-list-page/task-list-page.component';

/**
 * Batch "Flyt til kalender" modal (#1297) — moves every selected task to
 * another calendar of the SAME property.
 *
 * `data.boards` is the page's property-scoped calendar list; the action is only
 * enabled while exactly one property is filtered, so every option belongs to
 * the selected tasks' property (the server re-checks that per task).
 *
 * When every selected task already sits on the same calendar, that calendar is
 * left out: picking it would be a no-op move. With a mixed (or unknown)
 * current calendar nothing is hidden — each option is a real move for at least
 * one task.
 *
 * The server clears every per-occurrence calendar override and the tasks adopt
 * the target calendar's colour, so the history follows the task too — the
 * product owner accepted that, and the action needs no confirm step beyond Save.
 */
@Component({
  standalone: false,
  selector: 'app-batch-board-modal',
  templateUrl: './batch-board-modal.component.html',
})
export class BatchBoardModalComponent {
  boardId: number | null = null;
  readonly boards: CalendarBoardModel[];

  constructor(
    public dialogRef: MatDialogRef<BatchBoardModalComponent>,
    @Inject(MAT_DIALOG_DATA) public data: TaskListBatchModalData,
    private taskListService: BackendConfigurationPnTaskListService,
  ) {
    const currentBoardIds = new Set(data.selectedTasks.map(t => t.boardId ?? null));
    const [sharedBoardId] = Array.from(currentBoardIds);
    const allOnOneKnownBoard = currentBoardIds.size === 1 && sharedBoardId != null;
    this.boards = (data.boards ?? []).filter(b => !allOnOneKnownBoard || b.id !== sharedBoardId);
  }

  get valid(): boolean {
    return this.boardId != null;
  }

  hide() {
    this.dialogRef.close();
  }

  submit() {
    if (!this.valid) {
      return;
    }
    const taskIds = this.data.selectedTasks.map(t => t.id);
    this.taskListService.moveToBoard({taskIds, boardId: this.boardId!}).subscribe(res => {
      if (res && res.success) {
        this.dialogRef.close(true);
      }
    });
  }
}
