import {afterEach, beforeEach, describe, expect, it, vi} from 'vitest';
import type {Mock} from 'vitest';
// Same stubs as calendar-container.component.spec.ts: ng-gallery (reached through the modals' imports) cannot be
// loaded unlinked by the Angular test builder, and the gallery is not under test.
vi.mock('ng-gallery', () => ({Gallery: class {}, GalleryModule: class {}, ImageItem: class {}, GALLERY_CONFIG: 'GALLERY_CONFIG'}));
vi.mock('ng-gallery/lightbox', () => ({Lightbox: class {}, LightboxModule: class {}}));
import {ComponentFixture, TestBed} from '@angular/core/testing';
import {NO_ERRORS_SCHEMA} from '@angular/core';
import {NgSwitch, NgSwitchCase} from '@angular/common';
import {MatDialog} from '@angular/material/dialog';
import {Overlay} from '@angular/cdk/overlay';
import {Router} from '@angular/router';
import {Store} from '@ngrx/store';
import {TranslateModule} from '@ngx-translate/core';
import {BehaviorSubject, Observable, of, Subject, throwError} from 'rxjs';
import {EFormService} from 'src/app/common/services';
import {ItemsPlanningPnTagsService} from 'src/app/plugins/modules/items-planning-pn/services';
import {
  BackendConfigurationPnCalendarService,
  BackendConfigurationPnPropertiesService,
  BackendConfigurationPnWorkerTagsService,
} from '../../../../services';
import {CalendarLayoutService} from '../../services/calendar-layout.service';
import {BackendConfigurationCalendarLiveService, CalendarLiveHint} from '../../services/calendar-live.service';
import {CALENDAR_LIVE_RELOAD_DEBOUNCE_MS} from '../../services/calendar-live-reload.scheduler';
import {CalendarWeekGridComponent} from '../calendar-week-grid/calendar-week-grid.component';
import {CalendarStateService} from '../store';
import {CalendarContainerComponent} from './calendar-container.component';

const PROPERTY_A = 1;
const PROPERTY_B = 2;

/** Stands in for the hub: the spec pushes hints and reads the joins. */
class FakeLiveService {
  readonly hints = new Subject<CalendarLiveHint>();
  readonly hints$ = this.hints.asObservable();
  isLive = false;
  readonly joinProperty = vi.fn();
}

/** The container's private interaction state the live wiring reads. */
interface ContainerInternals {
  createOverlayRef: {dispose(): void} | null;
  closeCreateOverlay(): void;
}

describe('CalendarContainerComponent live updates', () => {
  let fixture: ComponentFixture<CalendarContainerComponent>;
  let component: CalendarContainerComponent;
  let live: FakeLiveService;
  let filters$: BehaviorSubject<Record<string, unknown>>;
  let getTasksForWeek: Mock;
  let getTasksForWeekQuiet: Mock;
  let moveTaskWithScope: Mock;
  let dialog: {open: Mock; openDialogs: unknown[]; afterAllClosed: Subject<void>};
  let hidden: boolean;

  const today = new Date();
  const todayIso =
    `${today.getFullYear()}-${(today.getMonth() + 1).toString().padStart(2, '0')}-${today.getDate().toString().padStart(2, '0')}`;
  /** Week requests of either kind: the user's (toasting) and the live reload's (quiet). */
  const reloads = () => getTasksForWeek.mock.calls.length + getTasksForWeekQuiet.mock.calls.length;
  const renderedTaskIds = () => component.tasksByDay.flat().map(t => t.id);
  const internals = () => component as unknown as ContainerInternals;
  const hint = (propertyId = PROPERTY_A, reason: CalendarLiveHint['reason'] = 'changed') =>
    live.hints.next({propertyId, reason});
  const setVisibility = (isHidden: boolean) => {
    hidden = isHidden;
    document.dispatchEvent(new Event('visibilitychange'));
  };

  beforeEach(async () => {
    live = new FakeLiveService();
    hidden = false;
    Object.defineProperty(document, 'hidden', {configurable: true, get: () => hidden});
    filters$ = new BehaviorSubject<Record<string, unknown>>({
      propertyId: null,
      currentDate: todayIso,
      viewMode: 'week',
      activeBoardIds: [],
      activeSiteIds: [],
      activeTeamIds: [],
      activeTagNames: [],
    });
    const stateService = {
      filters$,
      updatePropertyId: vi.fn((propertyId: number | null) => filters$.next({...filters$.value, propertyId})),
      setActiveBoardIds: vi.fn((activeBoardIds: number[]) => filters$.next({...filters$.value, activeBoardIds})),
      restoreFilters: vi.fn((partial: Record<string, unknown>) => filters$.next({...filters$.value, ...partial})),
      updateCurrentDate: vi.fn((currentDate: string) => filters$.next({...filters$.value, currentDate})),
      updateViewMode: vi.fn((viewMode: string) => filters$.next({...filters$.value, viewMode})),
      restoreSavedFilters: vi.fn(),
    };
    const weekAnswer = () => of({
      success: true,
      model: [{id: 101, propertyId: PROPERTY_A, taskDate: todayIso, startHour: 9, duration: 1, boardId: 10, title: 'A'}],
    });
    getTasksForWeek = vi.fn(weekAnswer);
    getTasksForWeekQuiet = vi.fn(weekAnswer);
    moveTaskWithScope = vi.fn((): Observable<{success: boolean}> => of({success: true}));
    dialog = {open: vi.fn(), openDialogs: [], afterAllClosed: new Subject<void>()};

    await TestBed.configureTestingModule({
      imports: [CalendarContainerComponent, TranslateModule.forRoot()],
      providers: [
        {provide: MatDialog, useValue: dialog},
        {provide: Overlay, useValue: {scrollStrategies: {reposition: vi.fn().mockReturnValue({})}}},
        {provide: Router, useValue: {navigate: vi.fn()}},
        {provide: Store, useValue: {select: vi.fn().mockReturnValue(of(false)), dispatch: vi.fn()}},
        {provide: BackendConfigurationPnCalendarService, useValue: {
          getBoards: vi.fn(() => of({success: true, model: [{id: 10, name: 'Default', color: '#123456'}]})),
          getTasksForWeek,
          getTasksForWeekQuiet,
          moveTaskWithScope,
        }},
        {provide: BackendConfigurationPnPropertiesService, useValue: {
          getAllPropertiesDictionary: vi.fn(() => of({success: true, model: [
            {id: PROPERTY_A, name: 'Property A', description: ''},
            {id: PROPERTY_B, name: 'Property B', description: ''},
          ]})),
          getLinkedFolderDtos: vi.fn(() => of({success: true, model: []})),
          getDeviceUsersFiltered: vi.fn(() => of({success: true, model: []})),
        }},
        {provide: CalendarStateService, useValue: stateService},
        {provide: CalendarLayoutService, useValue: {computeLayout: (tasks: unknown[]) => tasks}},
        {provide: ItemsPlanningPnTagsService, useValue: {getPlanningsTags: vi.fn(() => of({success: true, model: []}))}},
        {provide: BackendConfigurationPnWorkerTagsService, useValue: {getWorkerTags: vi.fn(() => of({success: true, model: []}))}},
        {provide: EFormService, useValue: {getAll: vi.fn(() => of({success: true, model: {templates: []}}))}},
      ],
    })
      // The child views are not under test; the live service is the fake.
      .overrideComponent(CalendarContainerComponent, {set: {
        imports: [NgSwitch, NgSwitchCase],
        schemas: [NO_ERRORS_SCHEMA],
        providers: [{provide: BackendConfigurationCalendarLiveService, useValue: live}],
      }})
      .compileComponents();

    vi.useFakeTimers();
    fixture = TestBed.createComponent(CalendarContainerComponent);
    component = fixture.componentInstance;
    fixture.detectChanges(); // ngOnInit selects property A and loads it once
  });

  afterEach(() => {
    fixture.destroy();
    vi.useRealTimers();
    Reflect.deleteProperty(document, 'hidden');
  });

  it('follows the property on screen', () => {
    expect(live.joinProperty).toHaveBeenLastCalledWith(PROPERTY_A);
    component.onPropertySelected(PROPERTY_B);
    expect(live.joinProperty).toHaveBeenLastCalledWith(PROPERTY_B);
  });

  it('reloads the tasks once, a second after a hint for the property on screen', () => {
    const before = reloads();
    hint();
    vi.advanceTimersByTime(CALENDAR_LIVE_RELOAD_DEBOUNCE_MS - 1);
    expect(reloads()).toBe(before);
    vi.advanceTimersByTime(1);
    expect(reloads()).toBe(before + 1);
  });

  it('merges a burst of hints into one reload', () => {
    const before = reloads();
    hint();
    vi.advanceTimersByTime(200);
    hint();
    hint();
    vi.advanceTimersByTime(5 * CALENDAR_LIVE_RELOAD_DEBOUNCE_MS);
    expect(reloads()).toBe(before + 1);
  });

  it('ignores a hint for another property', () => {
    const before = reloads();
    hint(PROPERTY_B);
    vi.advanceTimersByTime(5 * CALENDAR_LIVE_RELOAD_DEBOUNCE_MS);
    expect(reloads()).toBe(before);
  });

  it('reloads on a resync after a reconnect', () => {
    const before = reloads();
    hint(PROPERTY_A, 'resync');
    vi.advanceTimersByTime(CALENDAR_LIVE_RELOAD_DEBOUNCE_MS);
    expect(reloads()).toBe(before + 1);
  });

  it('lets a load of the calendar\'s own inside the window stand for the hint', () => {
    const before = reloads();
    hint();
    vi.advanceTimersByTime(300);
    component.onGoToToday(); // the user's own load
    vi.advanceTimersByTime(5 * CALENDAR_LIVE_RELOAD_DEBOUNCE_MS);
    expect(reloads()).toBe(before + 1);
  });

  it('holds the reload while a task popover is open and runs it once it closes', () => {
    internals().createOverlayRef = {dispose: vi.fn()};
    const before = reloads();
    hint();
    vi.advanceTimersByTime(5 * CALENDAR_LIVE_RELOAD_DEBOUNCE_MS);
    expect(reloads()).toBe(before);
    internals().closeCreateOverlay(); // closed without a save
    vi.advanceTimersByTime(CALENDAR_LIVE_RELOAD_DEBOUNCE_MS);
    expect(reloads()).toBe(before + 1);
    vi.advanceTimersByTime(5 * CALENDAR_LIVE_RELOAD_DEBOUNCE_MS);
    expect(reloads()).toBe(before + 1);
  });

  it('holds the reload while a dialog is open and runs it once all dialogs closed', () => {
    dialog.openDialogs.push({});
    const before = reloads();
    hint();
    vi.advanceTimersByTime(5 * CALENDAR_LIVE_RELOAD_DEBOUNCE_MS);
    expect(reloads()).toBe(before);
    dialog.openDialogs.length = 0;
    dialog.afterAllClosed.next();
    vi.advanceTimersByTime(CALENDAR_LIVE_RELOAD_DEBOUNCE_MS);
    expect(reloads()).toBe(before + 1);
  });

  it('holds the reload during a drag or resize and runs it after the pointer is released', () => {
    const grid = {isInteracting: true, clearSelection: vi.fn()};
    component.weekGrid = grid as unknown as CalendarWeekGridComponent;
    const before = reloads();
    hint();
    vi.advanceTimersByTime(5 * CALENDAR_LIVE_RELOAD_DEBOUNCE_MS);
    expect(reloads()).toBe(before);
    grid.isInteracting = false;
    document.dispatchEvent(new MouseEvent('mouseup'));
    vi.advanceTimersByTime(CALENDAR_LIVE_RELOAD_DEBOUNCE_MS);
    expect(reloads()).toBe(before + 1);
  });

  it('holds the reload while a move is being saved, and the save\'s own reload stands for it', () => {
    const save = new Subject<{success: boolean}>();
    moveTaskWithScope.mockReturnValue(save);
    component.onTaskMoved({taskId: 101, newDate: todayIso, newStartHour: 10, originalDate: todayIso});
    const before = reloads();
    hint();
    vi.advanceTimersByTime(5 * CALENDAR_LIVE_RELOAD_DEBOUNCE_MS);
    expect(reloads()).toBe(before);
    save.next({success: true});
    save.complete();
    expect(reloads()).toBe(before + 1); // the existing reload after the move
    vi.advanceTimersByTime(5 * CALENDAR_LIVE_RELOAD_DEBOUNCE_MS);
    expect(reloads()).toBe(before + 1);
  });

  it('holds reloads in a hidden tab and runs the owed one when it is shown again', () => {
    setVisibility(true);
    const before = reloads();
    hint();
    vi.advanceTimersByTime(5 * CALENDAR_LIVE_RELOAD_DEBOUNCE_MS);
    expect(reloads()).toBe(before);
    setVisibility(false);
    vi.advanceTimersByTime(CALENDAR_LIVE_RELOAD_DEBOUNCE_MS);
    expect(reloads()).toBe(before + 1);
  });

  it('reloads once when a live calendar\'s tab is shown again', () => {
    live.isLive = true;
    setVisibility(true);
    const before = reloads();
    setVisibility(false);
    vi.advanceTimersByTime(CALENDAR_LIVE_RELOAD_DEBOUNCE_MS);
    expect(reloads()).toBe(before + 1);
  });

  it('does not reload on a shown tab without a live hub (behaves as before)', () => {
    live.isLive = false;
    setVisibility(true);
    const before = reloads();
    setVisibility(false);
    vi.advanceTimersByTime(5 * CALENDAR_LIVE_RELOAD_DEBOUNCE_MS);
    expect(reloads()).toBe(before);
  });

  it('reloads in the background: a live reload uses the quiet request, never the toasting one', () => {
    const userLoads = getTasksForWeek.mock.calls.length;
    hint();
    vi.advanceTimersByTime(CALENDAR_LIVE_RELOAD_DEBOUNCE_MS);
    expect(getTasksForWeekQuiet).toHaveBeenCalledTimes(1);
    expect(getTasksForWeek.mock.calls.length).toBe(userLoads);
  });

  it('keeps the grid when a background reload fails (week)', () => {
    expect(renderedTaskIds()).toEqual([101]);
    getTasksForWeekQuiet.mockReturnValue(of({success: false, message: 'starting'}));
    hint();
    vi.advanceTimersByTime(CALENDAR_LIVE_RELOAD_DEBOUNCE_MS);
    expect(getTasksForWeekQuiet).toHaveBeenCalledTimes(1);
    expect(renderedTaskIds()).toEqual([101]);
  });

  it('keeps the grid when a background reload errors at the HTTP level (week)', () => {
    getTasksForWeekQuiet.mockReturnValue(throwError(() => new Error('502')));
    hint();
    vi.advanceTimersByTime(CALENDAR_LIVE_RELOAD_DEBOUNCE_MS);
    expect(renderedTaskIds()).toEqual([101]);
  });

  it('keeps the month when one week of a background reload fails', () => {
    component.onViewModeChange('month');
    const before = new Map(component.monthTasksByDate);
    expect(before.size).toBeGreaterThan(0);
    let call = 0;
    getTasksForWeekQuiet.mockImplementation(() =>
      ++call === 3 ? of({success: false}) : of({success: true, model: []}));
    hint();
    vi.advanceTimersByTime(CALENDAR_LIVE_RELOAD_DEBOUNCE_MS);
    expect(getTasksForWeekQuiet).toHaveBeenCalledTimes(6);
    expect(component.monthTasksByDate).toEqual(before);
  });

  it('still clears the grid when the user\'s own load fails (unchanged behaviour)', () => {
    getTasksForWeek.mockReturnValue(of({success: false, message: 'boom'}));
    component.onGoToToday();
    expect(renderedTaskIds()).toEqual([]);
  });

  it('reloads once after the first join when no load started since the join was sent', () => {
    const before = reloads();
    hint(PROPERTY_A, 'joining');
    hint(PROPERTY_A, 'joined');
    vi.advanceTimersByTime(CALENDAR_LIVE_RELOAD_DEBOUNCE_MS);
    expect(reloads()).toBe(before + 1);
  });

  it('does not reload after the first join when a load started after the join was sent', () => {
    hint(PROPERTY_A, 'joining');
    component.onGoToToday(); // e.g. the property switch's own load
    const before = reloads();
    hint(PROPERTY_A, 'joined');
    vi.advanceTimersByTime(5 * CALENDAR_LIVE_RELOAD_DEBOUNCE_MS);
    expect(reloads()).toBe(before);
  });

  it('holds a live reload while a user load is in flight, then runs exactly one background reload', () => {
    const slow = new Subject<unknown>();
    getTasksForWeek.mockReturnValue(slow);
    component.onGoToToday(); // the user's load, still running
    hint();
    vi.advanceTimersByTime(5 * CALENDAR_LIVE_RELOAD_DEBOUNCE_MS);
    expect(getTasksForWeekQuiet).not.toHaveBeenCalled();
    slow.next({success: true, model: [{id: 202, propertyId: PROPERTY_A, taskDate: todayIso, startHour: 9, duration: 1, boardId: 10, title: 'B'}]});
    slow.complete();
    expect(renderedTaskIds()).toEqual([202]);
    vi.advanceTimersByTime(CALENDAR_LIVE_RELOAD_DEBOUNCE_MS);
    expect(getTasksForWeekQuiet).toHaveBeenCalledTimes(1);
    vi.advanceTimersByTime(5 * CALENDAR_LIVE_RELOAD_DEBOUNCE_MS);
    expect(getTasksForWeekQuiet).toHaveBeenCalledTimes(1);
  });

  it('cannot discard a user load with a failing background reload', () => {
    const slow = new Subject<unknown>();
    getTasksForWeek.mockReturnValue(slow);
    getTasksForWeekQuiet.mockReturnValue(of({success: false}));
    component.onGoToToday();
    hint();
    vi.advanceTimersByTime(5 * CALENDAR_LIVE_RELOAD_DEBOUNCE_MS);
    slow.next({success: true, model: [{id: 202, propertyId: PROPERTY_A, taskDate: todayIso, startHour: 9, duration: 1, boardId: 10, title: 'B'}]});
    slow.complete();
    vi.advanceTimersByTime(CALENDAR_LIVE_RELOAD_DEBOUNCE_MS);
    expect(getTasksForWeekQuiet).toHaveBeenCalledTimes(1);
    expect(renderedTaskIds()).toEqual([202]);
  });

  it('holds a live reload while a user month load is in flight', () => {
    component.onViewModeChange('month');
    const slow = new Subject<unknown>();
    getTasksForWeek.mockReturnValue(slow);
    component.onNavigate(1);
    hint();
    vi.advanceTimersByTime(5 * CALENDAR_LIVE_RELOAD_DEBOUNCE_MS);
    expect(getTasksForWeekQuiet).not.toHaveBeenCalled();
    slow.next({success: true, model: []});
    slow.complete(); // all six weeks share the subject
    vi.advanceTimersByTime(CALENDAR_LIVE_RELOAD_DEBOUNCE_MS);
    expect(getTasksForWeekQuiet).toHaveBeenCalledTimes(6);
  });

  it('drops a pending reload when the view is destroyed', () => {
    const before = reloads();
    hint();
    fixture.destroy();
    vi.advanceTimersByTime(5 * CALENDAR_LIVE_RELOAD_DEBOUNCE_MS);
    expect(reloads()).toBe(before);
    expect(vi.getTimerCount()).toBe(0);
  });
});
