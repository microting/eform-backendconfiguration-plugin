import {TranslateService} from '@ngx-translate/core';
import {CalendarHeaderComponent} from './calendar-header.component';

// Class-level instantiation (matching the module's other specs, e.g.
// calendar-layout.service.spec.ts): the assertion is on the options array
// produced by buildViewModeOptions, which needs no Angular TestBed.
function makeTranslate(): TranslateService {
  // instant passthrough — the label is the key itself, which is all the
  // option-list assertions rely on. Interpolation params are applied so the
  // '{{count}} calendars' key can be asserted as the string users see.
  return {
    instant: (key: string, params?: Record<string, unknown>) =>
      params
        ? key.replace(/\{\{(\w+)}}/g, (_m, name) => String(params[name] ?? ''))
        : key,
  } as unknown as TranslateService;
}

function board(id: number, name: string, color = '#123456') {
  return {id, name, color} as any;
}

function person(id: number, name: string) {
  return {id, name, description: ''} as any;
}

describe('CalendarHeaderComponent', () => {
  let component: CalendarHeaderComponent;

  beforeEach(() => {
    component = new CalendarHeaderComponent(makeTranslate());
  });

  it('offers exactly the four calendar view modes', () => {
    // ngOnInit builds the option list.
    component.ngOnInit();

    const values = component.viewModeOptions.map(o => o.value);
    // The list is unconditional: every option is available to every user, and
    // the component no longer takes an isAdmin input to gate one with (#1170 —
    // compliance moved to its own page and is no longer a calendar view mode).
    expect(values).toEqual(['day', 'week', 'month', 'schedule']);
  });

  describe('boardsLabel', () => {
    // An EMPTY selection is NOT "nothing shown": GetTasksForWeek only narrows
    // when BoardIds is non-empty, so an empty filter renders every calendar.
    // The label must therefore match the all-checked label, not invite a pick.
    it('reads "All calendars" when nothing is selected, because nothing selected means no filter', () => {
      component.boards = [board(1, 'Default'), board(2, 'Drift')];
      component.activeBoardIds = [];

      expect(component.boardsLabel).toBe('All calendars');
    });

    it('reads "All calendars" when every calendar is selected', () => {
      component.boards = [board(1, 'Default'), board(2, 'Drift')];
      component.activeBoardIds = [1, 2];

      expect(component.boardsLabel).toBe('All calendars');
    });

    it('names the calendar when exactly one is selected', () => {
      component.boards = [board(1, 'Default'), board(2, 'Drift')];
      component.activeBoardIds = [2];

      expect(component.boardsLabel).toBe('Drift');
    });

    it('counts the selection when some but not all are selected', () => {
      component.boards = [board(1, 'Default'), board(2, 'Drift'), board(3, 'Tilsyn')];
      component.activeBoardIds = [1, 3];

      expect(component.boardsLabel).toBe('2 calendars');
    });

    // activeBoardIds survives in the store across a property switch long enough
    // to hold ids this property has no calendar for. Counting over `boards`
    // keeps those out of both the count and the all-selected comparison.
    it('ignores active ids that this property has no calendar for', () => {
      component.boards = [board(1, 'Default'), board(2, 'Drift')];
      component.activeBoardIds = [1, 2, 99];

      expect(component.boardsLabel).toBe('All calendars');
    });

    it('does not treat a stale id as part of the count', () => {
      component.boards = [board(1, 'Default'), board(2, 'Drift'), board(3, 'Tilsyn')];
      component.activeBoardIds = [1, 98, 99];

      expect(component.boardsLabel).toBe('Default');
    });
  });

  // #1211 — the assignee filter's button label. Deliberately NOT a copy of the
  // boardsLabel rules: there is no all-selected form, because an empty assignee
  // filter and a fully-ticked one render different grids.
  describe('assigneesLabel', () => {
    beforeEach(() => {
      component.teams = [person(10, 'Drift'), person(11, 'Tilsyn')];
      component.employees = [person(1, 'Anna'), person(2, 'Bo'), person(3, 'Carl')];
    });

    it('reads "All employees" when neither list has a selection', () => {
      component.activeTeamIds = [];
      component.activeSiteIds = [];

      expect(component.assigneesLabel).toBe('All employees');
    });

    it('names the employee when exactly one is selected', () => {
      component.activeSiteIds = [2];

      expect(component.assigneesLabel).toBe('Bo');
    });

    it('names the team when exactly one is selected', () => {
      component.activeTeamIds = [11];

      expect(component.assigneesLabel).toBe('Tilsyn');
    });

    // The two lists are ONE control, so the count spans both — a team plus a
    // person is "2 selected", not two separate labels.
    it('counts teams and employees together', () => {
      component.activeTeamIds = [10];
      component.activeSiteIds = [1];

      expect(component.assigneesLabel).toBe('2 selected');
    });

    it('counts a multi-employee selection', () => {
      component.activeSiteIds = [1, 3];

      expect(component.assigneesLabel).toBe('2 selected');
    });

    // Unlike the calendars label there is no all-selected shortcut: ticking
    // every employee is not the same filter as ticking none, because a task
    // assigned to nobody (or only through an unticked team) survives the empty
    // filter and not the full one.
    it('does not collapse a fully-ticked employee list to "All employees"', () => {
      component.activeSiteIds = [1, 2, 3];

      expect(component.assigneesLabel).toBe('3 selected');
    });

    // The label describes the FILTER, not the rendered rows. A worker tag
    // deleted from Property workers while it is still in `activeTeamIds` is
    // reachable (`teams` is loaded once, in ngOnInit) and the request still
    // carries it, so it has to keep counting — the old implementation counted
    // over the rendered lists and answered 'Bo' here, naming one of the three
    // active ids as if it were the whole filter.
    it('counts active ids that no rendered row matches', () => {
      component.activeSiteIds = [2, 99];
      component.activeTeamIds = [98];

      expect(component.assigneesLabel).toBe('3 selected');
    });

    // The worst case of the same bug: with every active id unresolvable the
    // old label read 'All employees' over a grid the server had narrowed.
    it('never reads "All employees" while ids are active but unrendered', () => {
      component.activeSiteIds = [99];
      component.activeTeamIds = [98];

      expect(component.assigneesLabel).toBe('2 selected');
    });

    // One active id, no row to name it with: there is no name to show, so it
    // falls back to the count form rather than claiming an empty filter.
    it('falls back to the count form when the single active id resolves to no row', () => {
      component.activeTeamIds = [98];

      expect(component.assigneesLabel).toBe('1 selected');
    });
  });

  describe('isEmployeeActive / isTeamActive', () => {
    it('reads each list independently, so a team id never ticks an employee row', () => {
      component.activeSiteIds = [4];
      component.activeTeamIds = [4];

      expect(component.isEmployeeActive(4)).toBe(true);
      expect(component.isTeamActive(4)).toBe(true);
      expect(component.isEmployeeActive(5)).toBe(false);
      expect(component.isTeamActive(5)).toBe(false);
    });
  });

  describe('isBoardActive', () => {
    it('is true only for ids in the active set', () => {
      component.activeBoardIds = [4, 7];

      expect(component.isBoardActive(4)).toBe(true);
      expect(component.isBoardActive(7)).toBe(true);
      expect(component.isBoardActive(5)).toBe(false);
    });
  });

  // NOTE (#1210): the inline rename popover, its editing state, its submit
  // handler and its onBoardEditKeydown guard are gone — the row ⋮ menu now
  // only emits editBoard / duplicateBoard / deleteBoard and the container owns
  // the dialogs. Their specs went with them rather than being rewritten into
  // assertions that an EventEmitter emits; the behaviour that replaced them is
  // covered by board-create-edit-modal.component.spec.ts,
  // board-delete-modal.component.spec.ts and calendar-board-name.helper.spec.ts.
  //
  // With no text input left inside the menu panel there is no FocusKeyManager
  // typeahead to stop, which is why dropping the keydown guard is not the
  // regression it would have been while the popover existed.
});
