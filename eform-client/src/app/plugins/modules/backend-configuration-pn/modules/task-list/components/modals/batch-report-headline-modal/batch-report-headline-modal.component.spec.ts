import {of, throwError} from 'rxjs';
import {CalendarTaskModel} from '../../../../../models/calendar';
import {TaskListBatchModalData} from '../../task-list-page/task-list-page.component';
import {BatchReportHeadlineModalComponent} from './batch-report-headline-modal.component';

/**
 * #1298 — "Skift rapportoverskrift" modal. Constructed directly (no TestBed):
 * the behaviour under test is the required gate, the addTag create call and
 * the submit payload, none of which needs the template.
 */
describe('BatchReportHeadlineModalComponent', () => {
  const tags = [
    {id: 3, name: 'BBB headline', description: '', isLocked: false},
    {id: 4, name: 'YYY headline', description: '', isLocked: false},
  ];
  const task = (id: number) => ({id, title: `T${id}`, tags: []} as unknown as CalendarTaskModel);

  let dialogRef: {close: jest.Mock};
  let taskListService: {changeReportHeadline: jest.Mock};
  let tagsService: {createPlanningTag: jest.Mock};
  let toastr: {error: jest.Mock};
  let translate: {instant: jest.Mock};

  const build = (selectedTasks: CalendarTaskModel[] = [task(1), task(2)]) => {
    const data: TaskListBatchModalData = {mode: 'changeReportHeadline', selectedTasks, tags};
    return new BatchReportHeadlineModalComponent(
      dialogRef as any, data, taskListService as any, tagsService as any, toastr as any, translate as any);
  };

  beforeEach(() => {
    dialogRef = {close: jest.fn()};
    taskListService = {changeReportHeadline: jest.fn().mockReturnValue(of({success: true}))};
    tagsService = {
      createPlanningTag: jest.fn().mockReturnValue(
        of({success: true, model: {id: 9, name: 'New headline', description: '', isLocked: false}})),
    };
    toastr = {error: jest.fn()};
    translate = {instant: jest.fn((k: string) => k)};
  });

  it('offers every headline it was given (headlines are global)', () => {
    expect(build().tags).toEqual([{id: 3, name: 'BBB headline'}, {id: 4, name: 'YYY headline'}]);
  });

  it('keeps Save disabled until a headline is picked — there is no "no headline" option', () => {
    const modal = build();
    expect(modal.itemPlanningTagId).toBeNull();
    expect(modal.valid).toBe(false);

    modal.submit();

    expect(taskListService.changeReportHeadline).not.toHaveBeenCalled();
    modal.itemPlanningTagId = 4;
    expect(modal.valid).toBe(true);
  });

  it('posts every selected task id with the picked headline and closes with true', () => {
    const modal = build([task(1), task(2)]);
    modal.itemPlanningTagId = 4;

    modal.submit();

    expect(taskListService.changeReportHeadline).toHaveBeenCalledWith({taskIds: [1, 2], itemPlanningTagId: 4});
    expect(dialogRef.close).toHaveBeenCalledWith(true);
  });

  it('stays open when the server refuses the change', () => {
    taskListService.changeReportHeadline.mockReturnValue(
      of({success: false, message: 'SelectedReportHeadlineNotFound'}));
    const modal = build();
    modal.itemPlanningTagId = 3;

    modal.submit();

    expect(dialogRef.close).not.toHaveBeenCalled();
  });

  it('addTag creates the headline through the same call as the calendar modal and selects it', async () => {
    const modal = build();

    const created = await modal.addTag('  New headline  ');

    expect(tagsService.createPlanningTag).toHaveBeenCalledWith({name: 'New headline'});
    expect(created).toEqual({id: 9, name: 'New headline'});
    expect(modal.tags.map(t => t.id)).toEqual([3, 4, 9]);
    expect(modal.itemPlanningTagId).toBe(9);
    expect(modal.valid).toBe(true);

    modal.submit();
    expect(taskListService.changeReportHeadline).toHaveBeenCalledWith({taskIds: [1, 2], itemPlanningTagId: 9});
  });

  it('addTag replaces (never mutates) the items array so mtx-select sees the new option', async () => {
    const modal = build();
    const before = modal.tags;

    await modal.addTag('New headline');

    expect(modal.tags).not.toBe(before);
    expect(before.map(t => t.id)).toEqual([3, 4]);
  });

  it('addTag rejects a blank name without calling the server', async () => {
    const modal = build();

    await expect(modal.addTag('   ')).rejects.toBeUndefined();

    expect(tagsService.createPlanningTag).not.toHaveBeenCalled();
    expect(modal.itemPlanningTagId).toBeNull();
  });

  it.each([
    ['an unsuccessful result', () => of({success: false})],
    ['an HTTP error', () => throwError(() => new Error('500'))],
  ])('addTag toasts and rejects on %s, leaving the selection empty', async (_label, response) => {
    tagsService.createPlanningTag.mockReturnValue(response());
    const modal = build();

    await expect(modal.addTag('New headline')).rejects.toBeUndefined();

    expect(toastr.error).toHaveBeenCalledWith('Could not create report headline');
    expect(modal.itemPlanningTagId).toBeNull();
    expect(modal.tags.map(t => t.id)).toEqual([3, 4]);
  });
});
