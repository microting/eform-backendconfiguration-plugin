import {of} from 'rxjs';
import {CalendarBoardModel, CalendarTaskModel} from '../../../../../models/calendar';
import {TaskListBatchModalData} from '../../task-list-page/task-list-page.component';
import {BatchBoardModalComponent} from './batch-board-modal.component';

/**
 * #1297 — "Flyt til kalender" modal. Constructed directly (no TestBed): the
 * behaviour under test is the option list and the submit payload, neither of
 * which needs the template.
 */
describe('BatchBoardModalComponent', () => {
  const boards: CalendarBoardModel[] = [
    {id: 10, name: 'Kalender A', color: '#111111', propertyId: 1},
    {id: 11, name: 'Kalender B', color: '#222222', propertyId: 1},
    {id: 12, name: 'Kalender C', color: '#333333', propertyId: 1},
  ];
  const task = (id: number, boardId: number | null) =>
    ({id, boardId, title: `T${id}`, tags: []} as unknown as CalendarTaskModel);

  let dialogRef: {close: jest.Mock};
  let taskListService: {moveToBoard: jest.Mock};

  const build = (selectedTasks: CalendarTaskModel[]) => {
    const data: TaskListBatchModalData = {mode: 'moveToBoard', selectedTasks, boards};
    return new BatchBoardModalComponent(dialogRef as any, data, taskListService as any);
  };
  const optionIds = (modal: BatchBoardModalComponent) => modal.boards.map(b => b.id);

  beforeEach(() => {
    dialogRef = {close: jest.fn()};
    taskListService = {moveToBoard: jest.fn().mockReturnValue(of({success: true}))};
  });

  it('hides the current calendar when every selected task shares it', () => {
    expect(optionIds(build([task(1, 10), task(2, 10)]))).toEqual([11, 12]);
  });

  it('shows every calendar when the selected tasks sit on different calendars', () => {
    expect(optionIds(build([task(1, 10), task(2, 11)]))).toEqual([10, 11, 12]);
  });

  it('shows every calendar when the shared current calendar is unknown', () => {
    expect(optionIds(build([task(1, null), task(2, null)]))).toEqual([10, 11, 12]);
  });

  it('keeps Save disabled until a calendar is picked', () => {
    const modal = build([task(1, 10)]);
    expect(modal.valid).toBe(false);
    modal.submit();
    expect(taskListService.moveToBoard).not.toHaveBeenCalled();
    modal.boardId = 11;
    expect(modal.valid).toBe(true);
  });

  it('posts every selected task id with the picked calendar and closes with true', () => {
    const modal = build([task(1, 10), task(2, 10)]);
    modal.boardId = 12;

    modal.submit();

    expect(taskListService.moveToBoard).toHaveBeenCalledWith({taskIds: [1, 2], boardId: 12});
    expect(dialogRef.close).toHaveBeenCalledWith(true);
  });

  it('stays open when the server refuses the move', () => {
    taskListService.moveToBoard.mockReturnValue(of({success: false, message: 'SelectedBoardNotFound'}));
    const modal = build([task(1, 10)]);
    modal.boardId = 11;

    modal.submit();

    expect(dialogRef.close).not.toHaveBeenCalled();
  });
});
