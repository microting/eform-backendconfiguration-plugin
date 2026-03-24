import {CalendarLayoutService} from './calendar-layout.service';
import {CalendarTaskModel} from '../../../models/calendar';

function makeTask(id: number, startHour: number, dur: number): CalendarTaskModel {
  return {
    id,
    startHour,
    dur,
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

  it('single task gets _colIndex=0 and _colCount=1', () => {
    const result = service.computeLayout([makeTask(1, 9, 1)]);
    expect(result).toHaveLength(1);
    expect(result[0]._colIndex).toBe(0);
    expect(result[0]._colCount).toBe(1);
  });

  it('two non-overlapping tasks each get _colCount=1', () => {
    // 09:00–10:00 and 11:00–12:00 — no overlap
    const result = service.computeLayout([makeTask(1, 9, 1), makeTask(2, 11, 1)]);
    expect(result.every(t => t._colCount === 1)).toBe(true);
    expect(result.every(t => t._colIndex === 0)).toBe(true);
  });

  it('two overlapping tasks split into two columns with _colCount=2', () => {
    // Both 09:00–10:00 — full overlap
    const result = service.computeLayout([makeTask(1, 9, 1), makeTask(2, 9, 1)]);
    const colIndexes = result.map(t => t._colIndex).sort((a, b) => a - b);
    expect(colIndexes).toEqual([0, 1]);
    expect(result.every(t => t._colCount === 2)).toBe(true);
  });

  it('three mutually overlapping tasks split into three columns', () => {
    const result = service.computeLayout([
      makeTask(1, 9, 2),
      makeTask(2, 9, 2),
      makeTask(3, 9, 2),
    ]);
    const colIndexes = result.map(t => t._colIndex).sort((a, b) => a - b);
    expect(colIndexes).toEqual([0, 1, 2]);
    expect(result.every(t => t._colCount === 3)).toBe(true);
  });

  it('partially overlapping group: two tasks overlap, third is separate', () => {
    // 09:00–11:00 and 10:00–12:00 overlap; 13:00–14:00 is separate
    const result = service.computeLayout([
      makeTask(1, 9, 2),
      makeTask(2, 10, 2),
      makeTask(3, 13, 1),
    ]);
    const standalone = result.find(t => t.id === 3)!;
    expect(standalone._colCount).toBe(1);
    expect(standalone._colIndex).toBe(0);
    const overlapping = result.filter(t => t.id !== 3);
    expect(overlapping.every(t => t._colCount === 2)).toBe(true);
  });

  it('output is sorted by startHour ascending', () => {
    const input = [makeTask(1, 11, 1), makeTask(2, 9, 1), makeTask(3, 8, 1)];
    const result = service.computeLayout(input);
    expect(result[0].startHour).toBe(8);
    expect(result[1].startHour).toBe(9);
    expect(result[2].startHour).toBe(11);
  });

  it('tasks with fractional hours (quarter-hour precision) are handled correctly', () => {
    // 09:00–09:30 and 09:30–10:00 — touching but NOT overlapping
    const result = service.computeLayout([makeTask(1, 9, 0.5), makeTask(2, 9.5, 0.5)]);
    expect(result.every(t => t._colCount === 1)).toBe(true);
  });

  it('task using colIndex can reuse a freed column slot', () => {
    // Task A: 09:00–10:00 (col 0), Task B: 09:00–10:00 (col 1), Task C: 10:00–11:00 (should reuse col 0)
    const result = service.computeLayout([
      makeTask(1, 9, 1),
      makeTask(2, 9, 1),
      makeTask(3, 10, 1),
    ]);
    const taskC = result.find(t => t.id === 3)!;
    // C should be in its own conflict group (no overlap with A or B at 10:00)
    expect(taskC._colCount).toBe(1);
    expect(taskC._colIndex).toBe(0);
  });
});
