import {ComponentFixture, TestBed} from '@angular/core/testing';
import {NO_ERRORS_SCHEMA} from '@angular/core';
import {MatDialog} from '@angular/material/dialog';
import {Overlay} from '@angular/cdk/overlay';
import {Router} from '@angular/router';
import {Store} from '@ngrx/store';
import {TranslateModule} from '@ngx-translate/core';
import {BehaviorSubject, of} from 'rxjs';
import {EFormService, EformTagService} from 'src/app/common/services';
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
      sidebarOpen: true,
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
      updateCurrentDate: jest.fn(),
      updateViewMode: jest.fn(),
      toggleBoard: jest.fn(),
      toggleSite: jest.fn(),
      toggleTeam: jest.fn(),
      toggleTag: jest.fn(),
      toggleSidebar: jest.fn(),
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
        {provide: EformTagService, useValue: {}},
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
});
