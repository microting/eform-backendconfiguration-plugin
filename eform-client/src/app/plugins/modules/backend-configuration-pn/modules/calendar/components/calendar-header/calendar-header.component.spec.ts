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
