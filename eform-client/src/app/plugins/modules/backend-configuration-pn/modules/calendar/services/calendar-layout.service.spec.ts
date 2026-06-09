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
    expect(result[0]._inGroup).toBe(false);
  });

  it('two non-overlapping tasks each get full width', () => {
    const result = service.computeLayout([makeTask(1, 9, 1), makeTask(2, 11, 1)]);
    expect(result.every(t => t._width === 100)).toBe(true);
    expect(result.every(t => t._left === 0)).toBe(true);
  });

  it('two overlapping tasks: extend-right cascade geometry', () => {
    const result = service.computeLayout([makeTask(1, 9, 1), makeTask(2, 9, 1)]);
    // left = i * 100/2; width = 100 - left, so each card extends to the right edge.
    expect(result[0]._left).toBe(0);
    expect(result[1]._left).toBe(50);
    expect(result[0]._width).toBe(100);
    expect(result[1]._width).toBe(50);
    expect(result[0]._zIndex).toBe(10);
    expect(result[1]._zIndex).toBe(11);
    // Both cards — including the leftmost (_width === 100) — are flagged as in a
    // multi-card overlap group so click-to-raise works for either.
    expect(result[0]._inGroup).toBe(true);
    expect(result[1]._inGroup).toBe(true);
  });

  it('three mutually overlapping tasks: extend-right cascade geometry', () => {
    const result = service.computeLayout([
      makeTask(1, 9, 2),
      makeTask(2, 9, 2),
      makeTask(3, 9, 2),
    ]);
    // left = i * 100/3; width = 100 - left.
    expect(result[0]._left).toBeCloseTo(0, 10);
    expect(result[1]._left).toBeCloseTo(100 / 3, 10);
    expect(result[2]._left).toBeCloseTo(200 / 3, 10);
    expect(result[0]._width).toBe(100);
    expect(result[1]._width).toBeCloseTo(100 - 100 / 3, 10); // 200/3 ≈ 66.667
    expect(result[2]._width).toBeCloseTo(100 - 200 / 3, 10); // 100/3 ≈ 33.333
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
    // Overlapping pair: lefts 0/50, widths extend to the right edge → 100/50.
    const overlapping = result.filter(t => t.id !== 3).sort((a, b) => a._left - b._left);
    expect(overlapping.map(t => t._left)).toEqual([0, 50]);
    expect(overlapping.map(t => t._width)).toEqual([100, 50]);
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
    // Lefts step by 100/4 (0, 25, 50, 75); widths extend to the right edge
    // (100 - left) → 100, 75, 50, 25.
    expect(result.map(t => t._left)).toEqual([0, 25, 50, 75]);
    expect(result.map(t => t._width)).toEqual([100, 75, 50, 25]);
  });
});
