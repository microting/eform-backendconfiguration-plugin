import {ComponentFixture, TestBed} from '@angular/core/testing';
import {NO_ERRORS_SCHEMA} from '@angular/core';
import {MatDialog} from '@angular/material/dialog';
import {Overlay} from '@angular/cdk/overlay';
import {Router} from '@angular/router';
import {Store} from '@ngrx/store';
import {TranslateModule, TranslateService} from '@ngx-translate/core';
import {BehaviorSubject, of, Subject} from 'rxjs';
import {EFormService} from 'src/app/common/services';
import {ItemsPlanningPnTagsService} from 'src/app/plugins/modules/items-planning-pn/services';
import {
  BackendConfigurationPnCalendarService,
  BackendConfigurationPnPropertiesService,
  BackendConfigurationPnWorkerTagsService,
} from '../../../../services';
import {CalendarLayoutService} from '../../services/calendar-layout.service';
import {CalendarStateService} from '../store';
import {CalendarContainerComponent} from './calendar-container.component';

// rebuildLayout() parses 'YYYY-MM-DD' with `new Date(...)`, which JS reads as
// UTC midnight, then compares it against a local-midnight Monday. At a negative
// UTC offset every fixture task lands on the previous local day, dayIdx is -1,
// and the `dayIdx >= 0` guard silently drops it — which would make the empty-grid
// assertions below pass even against unfixed code. Pinning UTC keeps the two
// sides of that comparison in the same zone on every developer machine (CI runs
// UTC already). Set at module scope because the describe body computes
// propertyATask.taskDate while collecting, before any hook runs; Node re-reads
// TZ on assignment, so this takes effect immediately.
process.env.TZ = 'UTC';

/** Mirrors CalendarContainerComponent.getMondayOfWeek (private). */
function mondayOfThisWeek(): Date {
  const d = new Date();
  const day = d.getDay();
  const diff = day === 0 ? -6 : 1 - day;
  const monday = new Date(d);
  monday.setDate(d.getDate() + diff);
  monday.setHours(0, 0, 0, 0);
  return monday;
}

function toLocalDateString(d: Date): string {
  return `${d.getFullYear()}-${(d.getMonth() + 1).toString().padStart(2, '0')}-${d.getDate().toString().padStart(2, '0')}`;
}

const PROPERTY_A = 1;
const PROPERTY_B = 2;

describe('CalendarContainerComponent', () => {
  let component: CalendarContainerComponent;
  let fixture: ComponentFixture<CalendarContainerComponent>;

  let calendarServiceStub: any;
  let propertiesServiceStub: any;
  let stateServiceStub: any;
  let filters$: BehaviorSubject<any>;
  /** Board loads keyed by property id; anything not listed resolves successfully. */
  let boardsByProperty: Map<number, any>;
  let weekTasksResponse: any;

  const propertyATask = {
    id: 101,
    propertyId: PROPERTY_A,
    taskDate: toLocalDateString(mondayOfThisWeek()),
    startHour: 9,
    duration: 1,
    isAllDay: false,
    boardId: 10,
    color: '#123456',
    title: 'Property A task',
    repeatType: 0,
    repeatEvery: 1,
  };

  beforeEach(async () => {
    filters$ = new BehaviorSubject<any>({
      propertyId: null,
      currentDate: toLocalDateString(new Date()),
      viewMode: 'week',
      activeBoardIds: [],
      activeSiteIds: [],
      activeTeamIds: [],
      activeTagNames: [],
    });

    // Stands in for the real ngrx-backed CalendarStateService. Only the two
    // behaviours the tests depend on are modelled: filters$ emits
    // synchronously, and updatePropertyId() also clears the board/site/team/tag
    // filters, exactly as CalendarStateService.updatePropertyId does.
    stateServiceStub = {
      filters$,
      updatePropertyId: jest.fn((propertyId: number | null) =>
        filters$.next({
          ...filters$.value,
          propertyId,
          activeBoardIds: [],
          activeSiteIds: [],
          activeTeamIds: [],
          activeTagNames: [],
        })),
      setActiveBoardIds: jest.fn((activeBoardIds: number[]) =>
        filters$.next({...filters$.value, activeBoardIds})),
      // Emit, do not just record: ngrx Store.select is synchronous on dispatch,
      // so the component's `currentDate` / `viewMode` (and therefore the
      // ngSwitch that picks the week or the month child) are already updated
      // when updateCurrentDate()/updateViewMode() return. The cross-path tests
      // below depend on exactly that.
      updateCurrentDate: jest.fn((currentDate: string) =>
        filters$.next({...filters$.value, currentDate})),
      updateViewMode: jest.fn((viewMode: string) =>
        filters$.next({...filters$.value, viewMode})),
      toggleBoard: jest.fn(),
      toggleSite: jest.fn(),
      toggleTeam: jest.fn(),
    };

    boardsByProperty = new Map<number, any>([
      [PROPERTY_A, {success: true, model: [{id: 10, name: 'Default', color: '#123456'}]}],
    ]);
    weekTasksResponse = {success: true, model: [propertyATask]};

    calendarServiceStub = {
      getBoards: jest.fn((propertyId: number) =>
        of(boardsByProperty.get(propertyId) ?? {success: true, model: []})),
      getTasksForWeek: jest.fn(() => of(weekTasksResponse)),
      moveTaskWithScope: jest.fn(() => of({success: false, message: 'rejected'})),
      resizeTask: jest.fn(() => of({success: true})),
    };
    propertiesServiceStub = {
      getAllPropertiesDictionary: jest.fn().mockReturnValue(of({
        success: true,
        model: [
          {id: PROPERTY_A, name: 'Property A', description: ''},
          {id: PROPERTY_B, name: 'Property B', description: ''},
        ],
      })),
      getLinkedFolderDtos: jest.fn().mockReturnValue(of({
        success: true,
        model: [{id: 77, name: 'Logbøger', children: []}],
      })),
      getDeviceUsersFiltered: jest.fn().mockReturnValue(of({
        success: true,
        model: [{siteId: 5, fullName: 'Worker A', userFirstName: 'Worker', userLastName: 'A', siteName: 'Worker A'}],
      })),
    };

    await TestBed.configureTestingModule({
      declarations: [CalendarContainerComponent],
      imports: [TranslateModule.forRoot()],
      providers: [
        {provide: MatDialog, useValue: {open: jest.fn().mockReturnValue({afterClosed: () => of(false)})}},
        {provide: Overlay, useValue: {scrollStrategies: {reposition: jest.fn().mockReturnValue({})}}},
        {provide: Router, useValue: {navigate: jest.fn()}},
        {provide: Store, useValue: {select: jest.fn().mockReturnValue(of(false)), dispatch: jest.fn()}},
        {provide: BackendConfigurationPnCalendarService, useValue: calendarServiceStub},
        {provide: BackendConfigurationPnPropertiesService, useValue: propertiesServiceStub},
        {provide: CalendarStateService, useValue: stateServiceStub},
        {provide: CalendarLayoutService, useValue: {computeLayout: (tasks: any[]) => tasks}},
        {provide: ItemsPlanningPnTagsService, useValue: {getPlanningsTags: jest.fn().mockReturnValue(of({success: true, model: []}))}},
        {provide: BackendConfigurationPnWorkerTagsService, useValue: {getWorkerTags: jest.fn().mockReturnValue(of({success: true, model: []}))}},
        {provide: EFormService, useValue: {getAll: jest.fn().mockReturnValue(of({success: true, model: {templates: []}}))}},
      ],
      schemas: [NO_ERRORS_SCHEMA],
    }).compileComponents();

    fixture = TestBed.createComponent(CalendarContainerComponent);
    component = fixture.componentInstance;
    // ngOnInit auto-selects the first property, so the grid starts populated
    // with property A's events.
    fixture.detectChanges();
  });

  function renderedTaskIds(): number[] {
    return component.tasksByDay.flat().map(t => t.id)
      .concat(component.allDayTasksByDay.flat().map(t => t.id));
  }

  it('renders the first property on init', () => {
    expect(component.currentPropertyId).toBe(PROPERTY_A);
    expect(component.selectedPropertyName).toBe('Property A');
    expect(renderedTaskIds()).toEqual([propertyATask.id]);
  });

  describe('when the board load for the newly selected property fails', () => {
    beforeEach(() => {
      // Precondition: property A's task really is on the grid, so the empty
      // expectations below prove the switch cleared it rather than passing
      // because nothing was ever rendered.
      expect(renderedTaskIds()).toEqual([propertyATask.id]);

      boardsByProperty.set(PROPERTY_B, {success: false, message: 'boom'});
      component.onPropertySelected(PROPERTY_B);
    });

    // #1239: the header pill and the grid must never disagree about which
    // property is on screen. Before the fix, loadBoards() skipped its whole
    // body on !success, so property A's events stayed rendered while the pill
    // had already switched to property B.
    it('does not leave the previous property\'s events under the new property name', () => {
      expect(component.selectedPropertyName).toBe('Property B');
      expect(renderedTaskIds()).toEqual([]);
      expect(component.tasks).toEqual([]);
    });

    it('clears the previous property\'s calendars and Logbøger folder', () => {
      expect(component.boards).toEqual([]);
      expect(component.logboegerFolderId).toBeNull();
    });

    it('does not fetch tasks for a property whose calendars failed to load', () => {
      expect(calendarServiceStub.getTasksForWeek)
        .not.toHaveBeenCalledWith(PROPERTY_B, expect.anything(), expect.anything(),
          expect.anything(), expect.anything(), expect.anything());
    });
  });

  it('leaves the successful path unchanged', () => {
    boardsByProperty.set(PROPERTY_B, {success: true, model: [{id: 20, name: 'B default', color: '#abcdef'}]});
    weekTasksResponse = {success: true, model: [{...propertyATask, id: 202, propertyId: PROPERTY_B, boardId: 20}]};

    component.onPropertySelected(PROPERTY_B);

    expect(component.selectedPropertyName).toBe('Property B');
    expect(component.boards.map(b => b.id)).toEqual([20]);
    expect(renderedTaskIds()).toEqual([202]);
    expect(component.logboegerFolderId).toBe(77);
    // clearPropertyScopedData deliberately leaves `employees` alone; assert it
    // so an over-clear on the success path cannot slip through unnoticed.
    expect(component.employees.map(e => e.id)).toEqual([5]);
  });

  // Distinct from the board-failure case: the boards call succeeds, so
  // clearPropertyScopedData never runs and only getLinkedFolderDtos' own else
  // branch can null the id. A stale id here is a write bug - it is handed to the
  // task create/edit modals as `folderId`, filing the new task's eForm under the
  // PREVIOUS property's Logboger folder.
  it('drops the Logboger folder id when only the folder lookup fails', () => {
    expect(component.logboegerFolderId).toBe(77);

    boardsByProperty.set(PROPERTY_B, {success: true, model: [{id: 20, name: 'B default', color: '#abcdef'}]});
    weekTasksResponse = {success: true, model: [{...propertyATask, id: 202, propertyId: PROPERTY_B, boardId: 20}]};
    propertiesServiceStub.getLinkedFolderDtos.mockReturnValue(of({success: false, message: 'boom'}));

    component.onPropertySelected(PROPERTY_B);

    expect(component.logboegerFolderId).toBeNull();
    // Boards and tasks are unaffected: only the folder lookup failed.
    expect(component.boards.map(b => b.id)).toEqual([20]);
    expect(renderedTaskIds()).toEqual([202]);
  });

  it('clears the grid when the week task load itself fails', () => {
    expect(renderedTaskIds()).toEqual([propertyATask.id]);

    weekTasksResponse = {success: false, message: 'boom'};
    component.loadTasks();

    expect(renderedTaskIds()).toEqual([]);
    expect(component.tasks).toEqual([]);
  });

  it('clears the employee list when the employee load fails', () => {
    expect(component.employees.length).toBe(1);

    propertiesServiceStub.getDeviceUsersFiltered.mockReturnValue(of({success: false, message: 'boom'}));
    component.loadEmployees();

    expect(component.employees).toEqual([]);
  });

  // #1246 — response staleness. The week path used to assign whatever came
  // back, unconditionally; the month path had its own private guard. There is
  // now ONE `loadSeq` covering both, bumped by clearTasks().
  //
  // Every test below holds a real response open in a Subject and completes it
  // at a chosen point, so what is being asserted is response ORDER, not timing:
  // the request is genuinely still in flight when the user moves on.
  describe('late responses', () => {
    const propertyBBoards = {success: true, model: [{id: 20, name: 'B default', color: '#abcdef'}]};
    const propertyBTask = {...propertyATask, id: 202, propertyId: PROPERTY_B, boardId: 20};

    /** Makes the next getTasksForWeek call hang; returns the handle to resolve it. */
    function pendWeekLoad(): Subject<any> {
      const pending = new Subject<any>();
      calendarServiceStub.getTasksForWeek.mockReturnValueOnce(pending);
      return pending;
    }

    function switchToPropertyB() {
      boardsByProperty.set(PROPERTY_B, propertyBBoards);
      weekTasksResponse = {success: true, model: [propertyBTask]};
      component.onPropertySelected(PROPERTY_B);
    }

    // THE case from the issue: property A's events must not reappear under
    // property B's name just because A's request was the slower one.
    it('discards a week response for a property the user has already left', () => {
      const propertyALoad = pendWeekLoad();
      component.loadTasks();

      switchToPropertyB();
      expect(component.selectedPropertyName).toBe('Property B');
      expect(renderedTaskIds()).toEqual([propertyBTask.id]);

      propertyALoad.next({success: true, model: [propertyATask]});
      propertyALoad.complete();

      expect(renderedTaskIds()).toEqual([propertyBTask.id]);
      expect(component.tasks.map(t => t.id)).toEqual([propertyBTask.id]);
    });

    // The guard sits ABOVE the !success branch on purpose: a superseded request
    // must not clear a grid that no longer belongs to it either.
    it('does not clear the new property\'s grid when the old property\'s request fails late', () => {
      const propertyALoad = pendWeekLoad();
      component.loadTasks();

      switchToPropertyB();

      propertyALoad.next({success: false, message: 'boom'});
      propertyALoad.complete();

      expect(renderedTaskIds()).toEqual([propertyBTask.id]);
    });

    // clearTasks() bumps the same counter, so a clear cannot be undone by a
    // week load that was already in flight. Reached here through the #1239
    // path: the board load for the newly selected property fails, which wipes
    // every property-scoped field.
    it('lets clearTasks() invalidate a week load that is still in flight', () => {
      const propertyALoad = pendWeekLoad();
      component.loadTasks();

      boardsByProperty.set(PROPERTY_B, {success: false, message: 'boom'});
      component.onPropertySelected(PROPERTY_B);
      expect(renderedTaskIds()).toEqual([]);

      propertyALoad.next({success: true, model: [propertyATask]});
      propertyALoad.complete();

      expect(renderedTaskIds()).toEqual([]);
      expect(component.tasks).toEqual([]);
    });

    // The month guard predates this change; unifying the counter must not have
    // cost it. One of the six week calls is held open so the forkJoin cannot
    // resolve until after the property switch.
    it('still discards a superseded month batch', () => {
      const firstWeekOfMonth = pendWeekLoad();
      filters$.next({...filters$.value, viewMode: 'month'});
      component.loadTasks();
      expect(component.monthTasksByDate.size).toBe(0);

      switchToPropertyB();
      const propertyBMonth = new Map(component.monthTasksByDate);
      expect(propertyBMonth.size).toBeGreaterThan(0);

      firstWeekOfMonth.next({success: true, model: [propertyATask]});
      firstWeekOfMonth.complete();

      expect(component.monthTasksByDate).toEqual(propertyBMonth);
    });

    // Not covered by loadSeq: `boards` is property-scoped but not week-scoped,
    // so it is guarded on the property id the request was made for. Everything
    // downstream of the board response (default-board selection, the folder
    // lookup, the loadTasks it kicks off) is scoped to that same id.
    it('discards a board response for a property the user has already left', () => {
      const propertyABoards = new Subject<any>();
      calendarServiceStub.getBoards.mockReturnValueOnce(propertyABoards);
      component.loadBoards(PROPERTY_A);

      switchToPropertyB();
      expect(component.boards.map(b => b.id)).toEqual([20]);

      propertyABoards.next({success: true, model: [{id: 10, name: 'Default', color: '#123456'}]});
      propertyABoards.complete();

      expect(component.boards.map(b => b.id)).toEqual([20]);
    });

    // The folder lookup is nested inside the board callback and outlives it, so
    // it needs its own re-check: logboegerFolderId is handed to the task
    // create/edit modals as `folderId`.
    it('discards a Logboger folder lookup for a property the user has already left', () => {
      const propertyAFolder = new Subject<any>();
      propertiesServiceStub.getLinkedFolderDtos.mockReturnValueOnce(propertyAFolder);
      component.loadBoards(PROPERTY_A);

      switchToPropertyB();
      expect(component.logboegerFolderId).toBe(77);

      propertyAFolder.next({success: true, model: [{id: 99, name: 'Logbøger', children: []}]});
      propertyAFolder.complete();

      expect(component.logboegerFolderId).toBe(77);
    });

    it('discards an employee list for a property the user has already left', () => {
      const propertyAEmployees = new Subject<any>();
      propertiesServiceStub.getDeviceUsersFiltered.mockReturnValueOnce(propertyAEmployees);
      component.loadEmployees();

      switchToPropertyB();
      expect(component.employees.map(e => e.id)).toEqual([5]);

      propertyAEmployees.next({
        success: true,
        model: [{siteId: 99, fullName: 'Worker A-only', userFirstName: 'Worker', userLastName: 'A-only', siteName: 'Worker A-only'}],
      });
      propertyAEmployees.complete();

      expect(component.employees.map(e => e.id)).toEqual([5]);
    });
  });

  // #1246, second route to the same symptom — deterministic, no race. The week
  // path writes tasks/tasksByDay/allDayTasksByDay, the month path writes
  // monthTasksByDate; neither clears the other's, and a property switch that
  // SUCCEEDS never reached clearTasks() at all. So the buffer the other path
  // had left behind survived the switch, and the next view change painted it —
  // under the new property's name — for as long as the new load took (six
  // round-trips on the month path).
  describe('cross-path buffers on a scope change', () => {
    const propertyBBoards = {success: true, model: [{id: 20, name: 'B default', color: '#abcdef'}]};
    const propertyBTask = {...propertyATask, id: 202, propertyId: PROPERTY_B, boardId: 20};

    /** Makes the next getTasksForWeek call hang; returns the handle to resolve it. */
    function pendWeekLoad(): Subject<any> {
      const pending = new Subject<any>();
      calendarServiceStub.getTasksForWeek.mockReturnValueOnce(pending);
      return pending;
    }

    function switchToPropertyB() {
      boardsByProperty.set(PROPERTY_B, propertyBBoards);
      weekTasksResponse = {success: true, model: [propertyBTask]};
      component.onPropertySelected(PROPERTY_B);
    }

    function monthTaskIds(): number[] {
      return Array.from(component.monthTasksByDate.values()).flat().map(t => t.id);
    }

    // THE repro: month -> week -> other property -> month.
    it('does not render the previous property\'s events when switching back to month view', () => {
      // 1. Property A, month view: the month buffer fills with A's events.
      component.onViewModeChange('month');
      expect(component.viewMode).toBe('month');
      expect(monthTaskIds()).toContain(propertyATask.id);

      // 2. Week view. Nothing on the week path touches the month buffer.
      component.onViewModeChange('week');
      expect(renderedTaskIds()).toEqual([propertyATask.id]);

      // 3. Property B. The week load SUCCEEDS, so the #1239 failure clear never
      //    runs — this is what makes the defect deterministic.
      switchToPropertyB();
      expect(component.selectedPropertyName).toBe('Property B');
      expect(renderedTaskIds()).toEqual([propertyBTask.id]);

      // 4. Back to month, with B's month batch genuinely in flight: forkJoin
      //    needs all six weeks and the first one is held open.
      const propertyBMonth = pendWeekLoad();
      component.onViewModeChange('month');

      // Fails without the fix: monthTasksByDate still holds property A's events
      // from step 1, and app-calendar-month-view is bound to it the instant
      // viewMode flips — so A's events render under B's name until all six
      // responses land.
      expect(monthTaskIds()).toEqual([]);

      // And the clear did not cancel the load it precedes: clearTasks() bumps
      // loadSeq, so clearing AFTER the ticket is taken would strand this batch
      // and the month would stay empty for good.
      propertyBMonth.next({success: true, model: [propertyBTask]});
      propertyBMonth.complete();
      expect(monthTaskIds()).toContain(propertyBTask.id);
      expect(monthTaskIds()).not.toContain(propertyATask.id);
    });

    // Same defect in the other direction: week buffers survive a month-view
    // property switch and are rendered by the week grid on the way back.
    it('does not render the previous property\'s events when switching back to week view', () => {
      expect(renderedTaskIds()).toEqual([propertyATask.id]);

      component.onViewModeChange('month');
      switchToPropertyB();
      expect(monthTaskIds()).toContain(propertyBTask.id);

      const propertyBWeek = pendWeekLoad();
      component.onViewModeChange('week');

      expect(renderedTaskIds()).toEqual([]);

      propertyBWeek.next({success: true, model: [propertyBTask]});
      propertyBWeek.complete();
      expect(renderedTaskIds()).toEqual([propertyBTask.id]);
    });

    // The other half of the fix: the clear is keyed on the SCOPE (property +
    // render path), so an ordinary refetch of the same scope must not blank the
    // grid. Clearing on every load would trade this defect for a visible flash
    // on every week step.
    it('keeps the current events on screen while an ordinary week step is in flight', () => {
      expect(renderedTaskIds()).toEqual([propertyATask.id]);

      const nextWeek = pendWeekLoad();
      component.onNavigate(1);

      expect(renderedTaskIds()).toEqual([propertyATask.id]);

      nextWeek.next({success: true, model: []});
      nextWeek.complete();
      expect(renderedTaskIds()).toEqual([]);
    });

    // The month view's Tidsplan link switches viewMode but stays on the month
    // render path (monthScheduleTasksByDay is derived from monthTasksByDate),
    // so the data it is about to show is still the current property's current
    // month - clearing it would be a flash for nothing.
    it('keeps the month data when the month view opens its month-scoped Tidsplan', () => {
      component.onViewModeChange('month');
      expect(monthTaskIds()).toContain(propertyATask.id);

      pendWeekLoad();
      component.onMonthScheduleClicked();

      expect(component.viewMode).toBe('schedule');
      expect(component.scheduleScope).toBe('month');
      expect(monthTaskIds()).toContain(propertyATask.id);
    });

    // The other half of the fix, on its own: the scope key is property AND
    // render path, so a path switch has to drop the other path's buffer even
    // when the property never changes. Nothing here calls onPropertySelected
    // after setup, so that method's unconditional clearTasks() cannot stand in
    // for the key check. month -> week -> five week steps (now a different
    // month) -> month leaves monthTasksByDate holding the FIRST month's
    // events, and app-calendar-month-view paints them the instant viewMode
    // flips — under a header that already reads the later month.
    it('does not render an earlier month\'s events when returning to month view on the same property', () => {
      component.onViewModeChange('month');
      expect(monthTaskIds()).toContain(propertyATask.id);

      component.onViewModeChange('week');
      expect(renderedTaskIds()).toEqual([propertyATask.id]);

      // Five week steps is 35 days — more than the longest month, so the
      // anchor is in a different month than the buffer above was filled for,
      // whatever day of the month the suite happens to run on.
      for (let i = 0; i < 5; i++) {
        component.onNavigate(1);
      }

      // A distinct id, so the in-flight buffer cannot be mistaken for the
      // later month's own data arriving early.
      const laterMonthTask = {...propertyATask, id: 303};
      weekTasksResponse = {success: true, model: [laterMonthTask]};
      const laterMonthLoad = pendWeekLoad();
      component.onViewModeChange('month');

      // Fails without the scope-key check: the property never changed, so
      // nothing else in loadTasks() clears the month buffer and the first
      // month's events stay on screen for all six round-trips.
      expect(monthTaskIds()).toEqual([]);

      laterMonthLoad.next({success: true, model: [laterMonthTask]});
      laterMonthLoad.complete();
      expect(monthTaskIds()).toContain(laterMonthTask.id);
      expect(monthTaskIds()).not.toContain(propertyATask.id);
    });
  });

  // calendar-week-grid moves the block locally before emitting taskMoved, so a
  // rejected move must still trigger a refetch or the block stays drawn where
  // the server refused to put it.
  it('refetches after a rejected move so the optimistic block is undone', () => {
    const callsBefore = calendarServiceStub.getTasksForWeek.mock.calls.length;

    component.onTaskMoved({
      taskId: propertyATask.id,
      newDate: propertyATask.taskDate,
      newStartHour: 11,
      originalDate: propertyATask.taskDate,
    });

    expect(calendarServiceStub.moveTaskWithScope).toHaveBeenCalled();
    expect(calendarServiceStub.getTasksForWeek.mock.calls.length).toBeGreaterThan(callsBefore);
  });

  // #1210 — "Duplicate" has no endpoint. The client POSTs a second calendar
  // with the source's colour and a "(copy)" name, and copies NO events: the
  // only call it is allowed to make is createBoard.
  describe('duplicating a calendar', () => {
    beforeEach(() => {
      calendarServiceStub.createBoard = jest.fn().mockReturnValue(of({success: true, model: 99}));
      // ngx-translate returns the raw key for a key it has no translation for,
      // and does NOT interpolate it — which would make every generated name the
      // same string. Interpolate here so the assertions are on the name a user
      // would actually get.
      jest.spyOn(TestBed.inject(TranslateService), 'instant').mockImplementation(
        ((key: string, params?: Record<string, unknown>) =>
          params ? key.replace(/\{\{(\w+)}}/g, (_m, n) => String(params[n] ?? '')) : key) as any);
    });

    it('posts the source colour under a "(copy)" name to the same property', () => {
      component.onDuplicateBoard({id: 10, name: 'Default', color: '#123456', propertyId: PROPERTY_A});

      expect(calendarServiceStub.createBoard).toHaveBeenCalledWith({
        name: 'Default (copy)',
        color: '#123456',
        propertyId: PROPERTY_A,
      });
    });

    it('numbers the copy past a "(copy)" that already exists', () => {
      component.boards = [
        {id: 10, name: 'Default', color: '#123456', propertyId: PROPERTY_A},
        {id: 11, name: 'Default (copy)', color: '#123456', propertyId: PROPERTY_A},
      ];

      component.onDuplicateBoard(component.boards[0]);

      expect(calendarServiceStub.createBoard).toHaveBeenCalledWith({
        name: 'Default (copy 2)',
        color: '#123456',
        propertyId: PROPERTY_A,
      });
    });

    it('reloads the calendar list once the copy lands', () => {
      const callsBefore = calendarServiceStub.getBoards.mock.calls.length;

      component.onDuplicateBoard({id: 10, name: 'Default', color: '#123456', propertyId: PROPERTY_A});

      expect(calendarServiceStub.getBoards.mock.calls.length).toBeGreaterThan(callsBefore);
    });

    it('does nothing at all when no property is selected', () => {
      component.currentPropertyId = null as any;

      component.onDuplicateBoard({id: 10, name: 'Default', color: '#123456', propertyId: PROPERTY_A});

      expect(calendarServiceStub.createBoard).not.toHaveBeenCalled();
    });

    // The copy name is derived from `this.boards` as of the last COMPLETED
    // load, so a second Duplicate fired before the reload lands recomputes the
    // identical name and mints a second "Default (copy)". Clicking closes the
    // dropdown, but the user only has to reopen it — the latch is the guard.
    it('ignores a second Duplicate while the first POST is still in flight', () => {
      const pendingPost = new Subject<any>();
      calendarServiceStub.createBoard = jest.fn().mockReturnValue(pendingPost);
      const row = {id: 10, name: 'Default', color: '#123456', propertyId: PROPERTY_A};

      component.onDuplicateBoard(row);
      component.onDuplicateBoard(row);

      expect(calendarServiceStub.createBoard).toHaveBeenCalledTimes(1);
    });

    it('re-arms once the copy has landed and the calendar list has reloaded', () => {
      const row = {id: 10, name: 'Default', color: '#123456', propertyId: PROPERTY_A};

      // getBoards resolves synchronously in this harness, so the reload — and
      // with it loadBoards' onSettled — has already run by the time this
      // returns.
      component.onDuplicateBoard(row);
      expect(component.duplicatingBoard).toBe(false);

      component.onDuplicateBoard(row);
      expect(calendarServiceStub.createBoard).toHaveBeenCalledTimes(2);
    });

    it('re-arms when the POST itself fails, so the user can retry', () => {
      calendarServiceStub.createBoard = jest.fn().mockReturnValue(of({success: false, message: 'boom'}));
      const row = {id: 10, name: 'Default', color: '#123456', propertyId: PROPERTY_A};

      component.onDuplicateBoard(row);

      expect(component.duplicatingBoard).toBe(false);
    });

    // loadBoards' onSettled has to fire on the superseded path too, or the
    // latch strands and Duplicate is dead until the page is reloaded.
    it('re-arms even when the property changed while the reload was in flight', () => {
      const row = {id: 10, name: 'Default', color: '#123456', propertyId: PROPERTY_A};
      const pendingBoards = new Subject<any>();
      calendarServiceStub.getBoards.mockReturnValueOnce(pendingBoards);

      component.onDuplicateBoard(row);
      expect(component.duplicatingBoard).toBe(true);

      component.onPropertySelected(PROPERTY_B);
      pendingBoards.next({success: true, model: []});
      pendingBoards.complete();

      expect(component.duplicatingBoard).toBe(false);
    });
  });

  // loadBoards' success path ends in loadTasks(). An extra loadTasks() at the
  // call site was not just a wasted round-trip (six of them in month view) —
  // firing BEFORE the reloaded calendars land, it built boardColorMap from the
  // pre-edit `this.boards` and painted one frame in the old colour.
  describe('refetching after a calendar edit or delete', () => {
    it('loads the week exactly once after a successful edit', () => {
      const dialog = TestBed.inject(MatDialog);
      (dialog.open as jest.Mock).mockReturnValue({afterClosed: () => of(true)});
      calendarServiceStub.getTasksForWeek.mockClear();

      component.onEditBoard({id: 10, name: 'Default', color: '#123456', propertyId: PROPERTY_A});

      expect(calendarServiceStub.getTasksForWeek).toHaveBeenCalledTimes(1);
    });

    it('loads the week exactly once after a successful delete', () => {
      const dialog = TestBed.inject(MatDialog);
      (dialog.open as jest.Mock).mockReturnValue({afterClosed: () => of(true)});
      calendarServiceStub.getTasksForWeek.mockClear();

      component.onDeleteBoard({id: 10, name: 'Default', color: '#123456', propertyId: PROPERTY_A});

      expect(calendarServiceStub.getTasksForWeek).toHaveBeenCalledTimes(1);
    });
  });

  // The create/edit dialog cannot fetch the property's calendars itself:
  // GET boards/{propertyId} auto-creates a Default board for an empty property,
  // so a load inside the dialog would be a write. The opener hands over the list
  // it already has, and the duplicate-name guard reads it from there.
  describe('the create/edit dialog', () => {
    it('is handed the property id and the loaded calendars, with no board for create', () => {
      const dialog = TestBed.inject(MatDialog);

      component.onCreateBoard();

      const [, config] = (dialog.open as jest.Mock).mock.calls[0];
      expect(config.data.propertyId).toBe(PROPERTY_A);
      expect(config.data.boards).toBe(component.boards);
      expect(config.data.board).toBeUndefined();
    });

    it('is handed the row being edited', () => {
      const dialog = TestBed.inject(MatDialog);
      const row = {id: 10, name: 'Default', color: '#123456', propertyId: PROPERTY_A};

      component.onEditBoard(row);

      const [, config] = (dialog.open as jest.Mock).mock.calls[0];
      expect(config.data.board).toBe(row);
      expect(config.data.boards).toBe(component.boards);
    });
  });

  describe('the delete dialog', () => {
    const row = {id: 10, name: 'Default', color: '#123456', propertyId: PROPERTY_A};

    // It needs the calendar count to warn that deleting the last one mints a
    // fresh Default rather than leaving the property empty.
    it('is handed the calendar and how many the property has', () => {
      const dialog = TestBed.inject(MatDialog);

      component.onDeleteBoard(row);

      const [, config] = (dialog.open as jest.Mock).mock.calls[0];
      expect(config.data.board).toBe(row);
      expect(config.data.boardCount).toBe(component.boards.length);
    });

    // Leaving the deleted id in activeBoardIds narrows GetTasksForWeek to a
    // calendar that no longer exists, and the grid comes back empty. An empty
    // set is "no filter", which is the right resting state here.
    it('drops the deleted calendar from the active filter before refetching', () => {
      const dialog = TestBed.inject(MatDialog);
      (dialog.open as jest.Mock).mockReturnValue({afterClosed: () => of(true)});
      stateServiceStub.setActiveBoardIds([10, 11]);
      stateServiceStub.setActiveBoardIds.mockClear();

      component.onDeleteBoard(row);

      expect(stateServiceStub.setActiveBoardIds).toHaveBeenCalledWith([11]);
      expect(component.activeBoardIds).toEqual([11]);
    });

    it('leaves the filter alone when the deleted calendar was not in it', () => {
      const dialog = TestBed.inject(MatDialog);
      (dialog.open as jest.Mock).mockReturnValue({afterClosed: () => of(true)});
      stateServiceStub.setActiveBoardIds([11]);
      stateServiceStub.setActiveBoardIds.mockClear();

      component.onDeleteBoard(row);

      expect(stateServiceStub.setActiveBoardIds).not.toHaveBeenCalled();
    });
  });
});
