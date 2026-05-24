import {CalendarLayoutService} from './calendar-layout.service';
import {CalendarTaskModel} from '../../../models/calendar';

function makeTask(id: number, startHour: number, dur: number): CalendarTaskModel {
  return {
    id,
    startHour,
    duration: dur,
    title: `Task ${id}`,
    tags: [],
    assigneeIds: [],
    boardId: 1,
    color: '#4CAF50',
    descriptionHtml: '',
    repeatRule: 'none',
    taskDate: '2026-03-19',
    completed: false,
    propertyId: 1,
    startText: '',
    endText: '',
  } as CalendarTaskModel;
}

const OVERLAP_FACTOR = 1.8;

describe('CalendarLayoutService', () => {
  let service: CalendarLayoutService;

  beforeEach(() => {
    service = new CalendarLayoutService();
  });

  it('returns empty array for empty input', () => {
    expect(service.computeLayout([])).toEqual([]);
  });

  it('returns empty array for null/undefined input', () => {
    expect(service.computeLayout(null as any)).toEqual([]);
    expect(service.computeLayout(undefined as any)).toEqual([]);
  });

  it('single task gets full width and base z-index', () => {
    const result = service.computeLayout([makeTask(1, 9, 1)]);
    expect(result).toHaveLength(1);
    expect(result[0]._left).toBe(0);
    expect(result[0]._width).toBe(100);
    expect(result[0]._zIndex).toBe(10);
  });

  it('two non-overlapping tasks each get full width', () => {
    const result = service.computeLayout([makeTask(1, 9, 1), makeTask(2, 11, 1)]);
    expect(result.every(t => t._width === 100)).toBe(true);
    expect(result.every(t => t._left === 0)).toBe(true);
  });

  it('two overlapping tasks: equal-divide-with-overlap geometry', () => {
    const result = service.computeLayout([makeTask(1, 9, 1), makeTask(2, 9, 1)]);
    const expectedWidth = 100 * OVERLAP_FACTOR / 2; // 90
    const expectedStep = (100 - expectedWidth) / (2 - 1); // 10
    expect(result.every(t => t._width === expectedWidth)).toBe(true);
    expect(result[0]._left).toBe(0);
    expect(result[1]._left).toBe(expectedStep);
    expect(result[0]._zIndex).toBe(10);
    expect(result[1]._zIndex).toBe(11);
  });

  it('three mutually overlapping tasks: evenly distributed left offsets', () => {
    const result = service.computeLayout([
      makeTask(1, 9, 2),
      makeTask(2, 9, 2),
      makeTask(3, 9, 2),
    ]);
    const expectedWidth = 100 * OVERLAP_FACTOR / 3; // 60
    const expectedStep = (100 - expectedWidth) / (3 - 1); // 20
    expect(result.every(t => t._width === expectedWidth)).toBe(true);
    expect(result.map(t => t._left)).toEqual([0, expectedStep, expectedStep * 2]);
    expect(result.map(t => t._zIndex)).toEqual([10, 11, 12]);
  });

  it('partially overlapping group: two tasks overlap, third is separate', () => {
    // 09:00–11:00 and 10:00–12:00 overlap; 13:00–14:00 is separate
    const result = service.computeLayout([
      makeTask(1, 9, 2),
      makeTask(2, 10, 2),
      makeTask(3, 13, 1),
    ]);
    const standalone = result.find(t => t.id === 3)!;
    expect(standalone._width).toBe(100);
    expect(standalone._left).toBe(0);
    const overlapping = result.filter(t => t.id !== 3);
    const expectedWidth = 100 * OVERLAP_FACTOR / 2;
    expect(overlapping.every(t => t._width === expectedWidth)).toBe(true);
  });

  it('output is sorted by startHour ascending', () => {
    const input = [makeTask(1, 11, 1), makeTask(2, 9, 1), makeTask(3, 8, 1)];
    const result = service.computeLayout(input);
    expect(result[0].startHour).toBe(8);
    expect(result[1].startHour).toBe(9);
    expect(result[2].startHour).toBe(11);
  });

  it('touching-but-not-overlapping tasks each get full width', () => {
    // 09:00–09:30 and 09:30–10:00 — touching boundary, no overlap
    const result = service.computeLayout([makeTask(1, 9, 0.5), makeTask(2, 9.5, 0.5)]);
    expect(result.every(t => t._width === 100)).toBe(true);
  });

  it('task whose start is at the end of a prior conflict group starts a new group', () => {
    // Task A: 09:00–10:00, Task B: 09:00–10:00, Task C: 10:00–11:00
    const result = service.computeLayout([
      makeTask(1, 9, 1),
      makeTask(2, 9, 1),
      makeTask(3, 10, 1),
    ]);
    const taskC = result.find(t => t.id === 3)!;
    expect(taskC._width).toBe(100);
    expect(taskC._left).toBe(0);
  });

  it('rightmost cascaded card sits on top of its conflict group by default', () => {
    const result = service.computeLayout([
      makeTask(1, 9, 1),
      makeTask(2, 9, 1),
      makeTask(3, 9, 1),
      makeTask(4, 9, 1),
    ]);
    const zIndexes = result.map(t => t._zIndex);
    // Default stacking: later index (in sort order) → higher z-index.
    expect(zIndexes).toEqual([10, 11, 12, 13]);
  });
});
