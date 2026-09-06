import {
  buildTaskListRows,
  complianceSortKey,
  SORT_KEY_PREFIX,
  toSortKey,
} from './task-list-table.component';
import {CalendarTaskModel} from '../../../../models/calendar';
import {CommonDictionaryModel, SharedTagModel} from 'src/app/common/models';

/**
 * #1193 — unit tests for the ROW BUILDER, not the grid.
 *
 * The grid sorts client-side over `data[sortHeaderId]` and the failure mode
 * that matters is silent: a `sortProp.id` naming a key that is missing (or
 * numeric-looking) on the row makes the header a no-op, with no error
 * anywhere. So what is pinned here is the shape and content of the keys
 * themselves: names resolve, unknown ids fall back to the bare prefix, the
 * compliance key has exactly three ordered states, an all-digit headline
 * never becomes a number, and string keys are lower-cased.
 */

// Exact copy of cdk's `_isNumberValue` (element.mjs), which
// `MatTableDataSource.sortingDataAccessor` uses to decide whether to sort a
// value as a number. Any string key for which this returns true is a bug.
const isNumberValue = (value: unknown): boolean =>
  !isNaN(parseFloat(value as any)) && !isNaN(Number(value));

const properties: CommonDictionaryModel[] = [
  {id: 1, name: 'Ejendom Nord', description: ''},
  {id: 2, name: 'ejendom syd', description: ''},
];
const planningTags: SharedTagModel[] = [
  {id: 10, name: 'Arbejdsmiljøregnskab'} as SharedTagModel,
  {id: 11, name: '2027'} as SharedTagModel,
  {id: 12, name: 'Test'} as SharedTagModel,
];
const eforms = [
  {id: 100, label: 'Medarbejder APV'},
  {id: 101, label: 'apv kontor'},
];

function makeTask(overrides: Partial<CalendarTaskModel> = {}): CalendarTaskModel {
  return {
    id: 1,
    title: 'Task',
    startHour: 9,
    duration: 1,
    startText: '09:00',
    endText: '10:00',
    tags: [],
    assigneeIds: [],
    workerNames: [],
    boardId: 5,
    color: '',
    descriptionHtml: '',
    repeatRule: 'none',
    taskDate: '2026-06-09',
    completed: false,
    status: true,
    complianceEnabled: false,
    propertyId: 1,
    eformId: 100,
    itemPlanningTagId: 10,
    ...overrides,
  } as CalendarTaskModel;
}

const repeatTextOf = (t: CalendarTaskModel) => (t.repeatRule === 'none' ? 'Gentages ikke' : 'Ugentligt');

const build = (tasks: CalendarTaskModel[]) =>
  buildTaskListRows(tasks, properties, planningTags, eforms, repeatTextOf);

describe('SORT_KEY_PREFIX / toSortKey', () => {
  it('the prefix is a single non-digit, non-whitespace character', () => {
    expect(SORT_KEY_PREFIX).toHaveLength(1);
    expect(/[\d\s]/.test(SORT_KEY_PREFIX)).toBe(false);
  });

  it('prefixes and lower-cases', () => {
    expect(toSortKey('Medarbejder APV')).toBe(`${SORT_KEY_PREFIX}medarbejder apv`);
  });

  it('null / undefined / empty all yield the bare prefix', () => {
    expect(toSortKey(null)).toBe(SORT_KEY_PREFIX);
    expect(toSortKey(undefined)).toBe(SORT_KEY_PREFIX);
    expect(toSortKey('')).toBe(SORT_KEY_PREFIX);
  });
});

describe('complianceSortKey', () => {
  it('inactive task → 0 regardless of the stored flag (cell shows "--")', () => {
    expect(complianceSortKey({status: false, complianceEnabled: true})).toBe(0);
    expect(complianceSortKey({status: false, complianceEnabled: false})).toBe(0);
  });

  it('active + compliance off (Nej) → 1', () => {
    expect(complianceSortKey({status: true, complianceEnabled: false})).toBe(1);
  });

  it('active + compliance on (Ja) → 2', () => {
    expect(complianceSortKey({status: true, complianceEnabled: true})).toBe(2);
  });
});

describe('buildTaskListRows', () => {
  it('returns [] for null / undefined / empty tasks', () => {
    expect(buildTaskListRows(null, properties, planningTags, eforms, repeatTextOf)).toEqual([]);
    expect(buildTaskListRows(undefined, properties, planningTags, eforms, repeatTextOf)).toEqual([]);
    expect(build([])).toEqual([]);
  });

  it('tolerates missing lookup lists (every name key is the bare prefix)', () => {
    const [row] = buildTaskListRows([makeTask()], null, undefined, null, repeatTextOf);
    expect(row.propertyName).toBe(SORT_KEY_PREFIX);
    expect(row.overskriftName).toBe(SORT_KEY_PREFIX);
    expect(row.eformName).toBe(SORT_KEY_PREFIX);
  });

  it('keeps every original task field (the row is the task plus keys)', () => {
    const task = makeTask({id: 42, title: 'Keep me', tags: ['a', 'b'], workerNames: ['W']});
    const [row] = build([task]);
    expect(row).toMatchObject(task);
    expect(row.id).toBe(42);
    expect(row.status).toBe(true);
    expect(row.complianceEnabled).toBe(false);
  });

  it('resolves property / report headline / eForm names by id', () => {
    const [row] = build([makeTask({propertyId: 1, itemPlanningTagId: 10, eformId: 100})]);
    expect(row.propertyName).toBe(`${SORT_KEY_PREFIX}ejendom nord`);
    expect(row.overskriftName).toBe(`${SORT_KEY_PREFIX}arbejdsmiljøregnskab`);
    expect(row.eformName).toBe(`${SORT_KEY_PREFIX}medarbejder apv`);
  });

  it('unknown ids → prefix only (rows without a headline group together)', () => {
    const [row] = build([makeTask({propertyId: 999, itemPlanningTagId: 999, eformId: 999})]);
    expect(row.propertyName).toBe(SORT_KEY_PREFIX);
    expect(row.overskriftName).toBe(SORT_KEY_PREFIX);
    expect(row.eformName).toBe(SORT_KEY_PREFIX);
  });

  it('null / undefined ids → prefix only', () => {
    const [nulls] = build([makeTask({itemPlanningTagId: null, eformId: null})]);
    expect(nulls.overskriftName).toBe(SORT_KEY_PREFIX);
    expect(nulls.eformName).toBe(SORT_KEY_PREFIX);
    const [undef] = build([makeTask({itemPlanningTagId: undefined, eformId: undefined})]);
    expect(undef.overskriftName).toBe(SORT_KEY_PREFIX);
    expect(undef.eformName).toBe(SORT_KEY_PREFIX);
  });

  it('repeatText is the DISPLAYED repeat text (from the callback), prefixed + lower-cased', () => {
    const [none, weekly] = build([makeTask({repeatRule: 'none'}), makeTask({repeatRule: 'weeklyOne'})]);
    expect(none.repeatText).toBe(`${SORT_KEY_PREFIX}gentages ikke`);
    expect(weekly.repeatText).toBe(`${SORT_KEY_PREFIX}ugentligt`);
  });

  it('titleSort is the prefixed, lower-cased title; `title` itself is untouched', () => {
    const [row] = build([makeTask({title: 'Brandøvelse Q1'})]);
    expect(row.titleSort).toBe(`${SORT_KEY_PREFIX}brandøvelse q1`);
    expect(row.title).toBe('Brandøvelse Q1');
  });

  it('complianceSort carries the three states 0 (--) < 1 (Nej) < 2 (Ja)', () => {
    const [na, nej, ja] = build([
      makeTask({status: false, complianceEnabled: true}),
      makeTask({status: true, complianceEnabled: false}),
      makeTask({status: true, complianceEnabled: true}),
    ]);
    expect(na.complianceSort).toBe(0);
    expect(nej.complianceSort).toBe(1);
    expect(ja.complianceSort).toBe(2);
    expect(na.complianceSort).toBeLessThan(nej.complianceSort);
    expect(nej.complianceSort).toBeLessThan(ja.complianceSort);
  });

  describe('numeric-looking headline (the blocking pitfall)', () => {
    it('a headline named "2027" yields a key that cdk does NOT treat as a number', () => {
      const [row] = build([makeTask({itemPlanningTagId: 11})]);
      expect(row.overskriftName).toBe(`${SORT_KEY_PREFIX}2027`);
      expect(isNumberValue(row.overskriftName)).toBe(false);
      // The two half-measures the issue rules out, pinned so nobody re-tries them.
      expect(isNumberValue('2027')).toBe(true);
      expect(isNumberValue(' 2027')).toBe(true);
    });

    it('"2027", "Arbejdsmiljøregnskab", "Test" sort coherently and reversibly among each other', () => {
      const rows = build([
        makeTask({id: 1, itemPlanningTagId: 12}),
        makeTask({id: 2, itemPlanningTagId: 11}),
        makeTask({id: 3, itemPlanningTagId: 10}),
      ]);
      // Every key is a string (none number-like), so the default code-point
      // comparator gives a total order — no 0-returning mixed pair.
      rows.forEach(r => expect(isNumberValue(r.overskriftName)).toBe(false));
      const asc = [...rows].sort((a, b) =>
        a.overskriftName < b.overskriftName ? -1 : a.overskriftName > b.overskriftName ? 1 : 0);
      expect(asc.map(r => r.id)).toEqual([2, 3, 1]); // 2027, arbejdsmiljøregnskab, test
      const desc = [...rows].sort((a, b) =>
        a.overskriftName < b.overskriftName ? 1 : a.overskriftName > b.overskriftName ? -1 : 0);
      expect(desc.map(r => r.id)).toEqual([1, 3, 2]);
    });

    it('applies to every derived string key, not only the headline', () => {
      const [row] = buildTaskListRows(
        [makeTask({title: '42', propertyId: 7, eformId: 8, itemPlanningTagId: 9})],
        [{id: 7, name: '2029', description: ''}],
        [{id: 9, name: '2031'} as SharedTagModel],
        [{id: 8, label: '2033'}],
        () => '365',
      );
      for (const key of [row.titleSort, row.propertyName, row.eformName, row.overskriftName, row.repeatText]) {
        expect(isNumberValue(key)).toBe(false);
      }
    });
  });

  describe('lower-casing (case-insensitive, not locale-collated)', () => {
    it('"Ejendom Nord" and "ejendom syd" order by their letters, not by case', () => {
      const [nord, syd] = build([makeTask({propertyId: 1}), makeTask({propertyId: 2})]);
      // Without lower-casing 'E' (0x45) < 'e' (0x65) would put Nord first for
      // the wrong reason; with it, 'n' < 's' decides.
      expect(nord.propertyName < syd.propertyName).toBe(true);
      const [upperZ, lowerA] = build([makeTask({title: 'Zebra'}), makeTask({title: 'apple'})]);
      expect(lowerA.titleSort < upperZ.titleSort).toBe(true);
    });

    it('eForm labels are lower-cased too', () => {
      const [apv] = build([makeTask({eformId: 101})]);
      expect(apv.eformName).toBe(`${SORT_KEY_PREFIX}apv kontor`);
    });
  });
});
