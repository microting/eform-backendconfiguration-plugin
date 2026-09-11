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

  describe('inline calendar edit', () => {
    it('startEditBoard seeds the popover from the row', () => {
      component.startEditBoard(board(9, 'Drift', '#aabbcc'));

      expect(component.editingBoardId).toBe(9);
      expect(component.editingBoardName).toBe('Drift');
      expect(component.editingBoardColor).toBe('#aabbcc');
    });

    it('submitEditBoard emits the trimmed name and clears the editing row', () => {
      const emitted: {id: number; name: string; color: string}[] = [];
      component.updateBoard.subscribe(e => emitted.push(e));

      component.startEditBoard(board(9, 'Drift', '#aabbcc'));
      component.editingBoardName = '  Drift 2  ';
      component.submitEditBoard();

      expect(emitted).toEqual([{id: 9, name: 'Drift 2', color: '#aabbcc'}]);
      expect(component.editingBoardId).toBeNull();
    });

    it('submitEditBoard emits nothing for a blank name', () => {
      const emitted: unknown[] = [];
      component.updateBoard.subscribe(e => emitted.push(e));

      component.startEditBoard(board(9, 'Drift'));
      component.editingBoardName = '   ';
      component.submitEditBoard();

      expect(emitted).toEqual([]);
      // Still closed: a blank name cancels the edit rather than trapping it.
      expect(component.editingBoardId).toBeNull();
    });

    it('submitEditBoard emits nothing when no row is being edited', () => {
      const emitted: unknown[] = [];
      component.updateBoard.subscribe(e => emitted.push(e));

      component.editingBoardName = 'Drift';
      component.submitEditBoard();

      expect(emitted).toEqual([]);
    });

    describe('onBoardEditKeydown', () => {
      function keydown(key: string) {
        let stopped = false;
        const event = {key, stopPropagation: () => (stopped = true)} as unknown as KeyboardEvent;
        component.onBoardEditKeydown(event);
        return stopped;
      }

      // The popover's input lives inside a MatMenu panel, so an unstopped
      // keystroke drives the panel's FocusKeyManager typeahead and yanks focus
      // off the input mid-word.
      it('stops typing from reaching the menu panel', () => {
        expect(keydown('a')).toBe(true);
        expect(keydown('ArrowDown')).toBe(true);
        expect(keydown('Enter')).toBe(true);
      });

      // Regression: a blanket stopPropagation() here swallowed Escape as well.
      // The CDK's OverlayKeyboardDispatcher listens on `body` in the bubble
      // phase, so the event never reached ANY overlay — the popover and the
      // calendars panel behind it both became impossible to close by keyboard.
      it('lets Escape through so the overlay can close', () => {
        expect(keydown('Escape')).toBe(false);
      });
    });
  });
});
