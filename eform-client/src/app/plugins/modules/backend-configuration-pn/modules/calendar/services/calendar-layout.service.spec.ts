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

  it('partial chain A/B/C: freed column is reused (2 columns, not 3)', () => {
    // A 09:00–10:00, B 09:30–11:00, C 10:30–11:30.
    // A and C do NOT overlap each other; both overlap B → true concurrency is 2.
    // First-fit packs: A→col0, B→col1, C reuses col0 (A ended 10:00 ≤ C start 10:30).
    // numCols = 2 → _left = columnIndex/2*100, _width = 100 - _left.
    const result = service.computeLayout([
      makeTask(1, 9, 1),     // A 09:00–10:00
      makeTask(2, 9.5, 1.5), // B 09:30–11:00
      makeTask(3, 10.5, 1),  // C 10:30–11:30
    ]);
    const a = result.find(t => t.id === 1)!;
    const b = result.find(t => t.id === 2)!;
    const c = result.find(t => t.id === 3)!;
    // A in col0: full width, left 0.
    expect(a._left).toBe(0);
    expect(a._width).toBe(100);
    // B in col1 of 2: left 50, extends to the right edge → width 50.
    expect(b._left).toBe(50);
    expect(b._width).toBe(50);
    // C reuses col0 (A's freed column): left 0, full width — NOT a 33% sliver.
    expect(c._left).toBe(0);
    expect(c._width).toBe(100);
    // z-index tracks columnIndex: col0 → 10, col1 → 11.
    expect(a._zIndex).toBe(10);
    expect(c._zIndex).toBe(10);
    expect(b._zIndex).toBe(11);
    // All three share a cluster → all flagged in-group for click-to-raise.
    expect(result.every(t => t._inGroup === true)).toBe(true);
  });

  it('Google-reference cluster: one long base + short non-mutually-overlapping tasks → 2 columns', () => {
    // Tjekke grise 09:00–14:00 (base) plus four short tasks that each overlap the
    // base but not one another. First-fit: base→col0, every short task packs into
    // col1 (each starts at/after the previous short task ends). numCols = 2.
    const result = service.computeLayout([
      makeTask(1, 9, 5),      // base            09:00–14:00
      makeTask(2, 9.5, 0.5),  // Overbrusning    09:30–10:00
      makeTask(3, 11.5, 0.5), // Faringsrunde    11:30–12:00
      makeTask(4, 13, 0.5),   // Faring og split 13:00–13:30
      makeTask(5, 13.5, 1),   // Div. registr.   13:30–14:30
    ]);
    const base = result.find(t => t.id === 1)!;
    // Base sits full-width underneath in col0.
    expect(base._left).toBe(0);
    expect(base._width).toBe(100);
    expect(base._zIndex).toBe(10);
    // Every short task packs into col1 → left 50, width 50, z 11.
    const shorts = result.filter(t => t.id !== 1);
    expect(shorts).toHaveLength(4);
    expect(shorts.every(t => t._left === 50)).toBe(true);
    expect(shorts.every(t => t._width === 50)).toBe(true);
    expect(shorts.every(t => t._zIndex === 11)).toBe(true);
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

  it('no-regression invariant: identical timeslots produce the old cascade for N=2,3,4', () => {
    // For N mutually-overlapping identical tasks, first-fit assigns
    // columnIndex === list index and numCols === N → byte-identical to the
    // pre-packing output. Assert the exact geometry for N = 2, 3, 4.
    for (const n of [2, 3, 4]) {
      const tasks = Array.from({length: n}, (_, i) => makeTask(i + 1, 9, 2));
      const result = service.computeLayout(tasks);
      expect(result).toHaveLength(n);
      result.forEach((t, i) => {
        expect(t._left).toBeCloseTo((i / n) * 100, 10);
        expect(t._width).toBeCloseTo(100 - (i / n) * 100, 10);
        expect(t._zIndex).toBe(10 + i);
        expect(t._inGroup).toBe(true);
      });
    }
  });

  it('touching tasks in one cluster reuse the same column', () => {
    // A 09:00–10:00 and B 10:00–11:00 touch (A.end === B.start → not overlap),
    // but C 09:30–10:30 bridges them into a single cluster. First-fit: A→col0,
    // C→col1 (overlaps A), B reuses col0 (A ended 10:00 ≤ B start 10:00, touching).
    const result = service.computeLayout([
      makeTask(1, 9, 1),    // A 09:00–10:00
      makeTask(2, 9.5, 1),  // C 09:30–10:30 (bridge)
      makeTask(3, 10, 1),   // B 10:00–11:00
    ]);
    const a = result.find(t => t.id === 1)!;
    const c = result.find(t => t.id === 2)!;
    const b = result.find(t => t.id === 3)!;
    // A and B share col0 (touching reuse) → same left/width.
    expect(a._left).toBe(0);
    expect(a._width).toBe(100);
    expect(b._left).toBe(0);
    expect(b._width).toBe(100);
    // C is the concurrent one in col1 of 2.
    expect(c._left).toBe(50);
    expect(c._width).toBe(50);
  });

  it('3-column partial cluster reuses a freed column', () => {
    // One connected cluster whose true max concurrency is 3, not 4:
    //   L 09:00–13:00 (long base), A 09:30–10:30, B 09:45–10:45, C 11:00–12:00.
    // First-fit packing: L→col0, A→col1, B→col2 (col0 & col1 both busy at 09:45),
    // C→col1 reused (A freed its column at 10:30 ≤ C start 11:00). numCols = 3, so
    // C lands in col1 — NOT a 4th column that would crush every card into slivers.
    const result = service.computeLayout([
      makeTask(1, 9, 4),     // L 09:00–13:00
      makeTask(2, 9.5, 1),   // A 09:30–10:30
      makeTask(3, 9.75, 1),  // B 09:45–10:45
      makeTask(4, 11, 1),    // C 11:00–12:00
    ]);
    const l = result.find(t => t.id === 1)!;
    const a = result.find(t => t.id === 2)!;
    const b = result.find(t => t.id === 3)!;
    const c = result.find(t => t.id === 4)!;
    // L in col0: full width, left 0.
    expect(l._left).toBeCloseTo(0, 10);
    expect(l._width).toBeCloseTo(100, 10);
    expect(l._zIndex).toBe(10);
    // A in col1 of 3: left 100/3, extends to the right edge → width 200/3.
    expect(a._left).toBeCloseTo(100 / 3, 10);
    expect(a._width).toBeCloseTo(100 - 100 / 3, 10); // 200/3 ≈ 66.667
    expect(a._zIndex).toBe(11);
    // C reuses A's freed col1 → same geometry as A, NOT a narrower 4th column.
    expect(c._left).toBeCloseTo(100 / 3, 10);
    expect(c._width).toBeCloseTo(100 - 100 / 3, 10);
    expect(c._zIndex).toBe(11);
    // B in col2 of 3: left 200/3, width 100/3.
    expect(b._left).toBeCloseTo(200 / 3, 10);
    expect(b._width).toBeCloseTo(100 - 200 / 3, 10); // 100/3 ≈ 33.333
    expect(b._zIndex).toBe(12);
    // All four share one cluster → all flagged in-group for click-to-raise.
    expect(result.every(t => t._inGroup === true)).toBe(true);
  });

  it('tie-break: when two tasks share a start, the longer one takes the earlier column', () => {
    // Same start 09:00, durations 2h and 1h. They mutually overlap → 2 columns.
    // Tie-break "longer duration first" is deterministic regardless of input
    // order: the 2h task lands in col0 (left 0), the 1h task in col1 (left 50).
    const longerFirst = service.computeLayout([makeTask(1, 9, 2), makeTask(2, 9, 1)]);
    const shorterFirst = service.computeLayout([makeTask(1, 9, 1), makeTask(2, 9, 2)]);
    for (const result of [longerFirst, shorterFirst]) {
      const longer = result.find(t => t.duration === 2)!;
      const shorter = result.find(t => t.duration === 1)!;
      expect(longer._left).toBe(0);
      expect(longer._width).toBe(100);
      expect(shorter._left).toBe(50);
      expect(shorter._width).toBe(50);
    }
  });
});
