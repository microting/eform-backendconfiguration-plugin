import {ComponentFixture, TestBed} from '@angular/core/testing';
import {NO_ERRORS_SCHEMA} from '@angular/core';
import {MatDialog} from '@angular/material/dialog';
import {Overlay} from '@angular/cdk/overlay';
import {TranslateModule} from '@ngx-translate/core';
import {Store} from '@ngrx/store';
import {BehaviorSubject, of} from 'rxjs';
import {EFormService} from 'src/app/common/services';
import {ItemsPlanningPnTagsService} from 'src/app/plugins/modules/items-planning-pn/services';
import {
  BackendConfigurationPnCalendarService,
  BackendConfigurationPnPropertiesService,
  BackendConfigurationPnTaskListService,
  BackendConfigurationPnWorkerTagsService,
} from '../../../../services';
import {CalendarRepeatService} from '../../../calendar/services/calendar-repeat.service';
import {
  TaskCreateEditModalComponent,
  TaskCreateEditModalData,
} from '../../../calendar/modals/task-create-edit-modal/task-create-edit-modal.component';
import {RepeatScopeModalComponent} from '../../../calendar/modals/repeat-scope-modal/repeat-scope-modal.component';
import {CalendarTaskModel} from '../../../../models/calendar';
import {TaskListPageComponent} from './task-list-page.component';

/**
 * #1302 — Opgaveliste for the eForm `user` role.
 *
 *  1. Role gating (rendered): the batch dropdown `#taskListBatchAction`, the
 *     selection counter `#taskListSelectionCount` and the grid's checkbox column
 *     (`[isAdmin]` → the table's `[rowSelectable]`) are admin-only; everything
 *     else on the toolbar stays. The role is read ONCE.
 *  2. #1140: the edit modal opens on the next upcoming occurrence of a
 *     past-started series instead of the series start, so it is editable — and
 *     the modal's own this / this-and-following / all dialog then appears on
 *     save, as in the calendar.
 *
 * TranslateModule.forRoot() has no loader, so the pipe echoes keys.
 */

const ROW_TASK_ID = 7;

function buildTask(overrides: Partial<CalendarTaskModel> = {}): CalendarTaskModel {
  return {
    id: ROW_TASK_ID,
    title: 'Ugentlig rengøring',
    startHour: 9,
    duration: 1,
    tags: [],
    assigneeIds: [12],
    workerNames: ['Clara Holm'],
    workerTagIds: [],
    boardId: 1,
    color: '',
    descriptionHtml: '',
    repeatRule: 'weeklyOne',
    repeatType: 2,
    repeatEvery: 1,
    taskDate: '2025-01-06',
    completed: false,
    status: true,
    complianceEnabled: false,
    propertyId: 5,
    upcomingOccurrenceDates: ['2026-09-21', '2026-09-28', '2026-10-05'],
    ...overrides,
  } as CalendarTaskModel;
}

describe('TaskListPageComponent — #1302', () => {
  let fixture: ComponentFixture<TaskListPageComponent>;
  let component: TaskListPageComponent;
  let dialogStub: any;
  let isAdmin$: BehaviorSubject<boolean>;

  async function setup(isAdmin: boolean) {
    isAdmin$ = new BehaviorSubject<boolean>(isAdmin);
    dialogStub = {open: jest.fn().mockReturnValue({afterClosed: () => of(false)})};

    await TestBed.configureTestingModule({
      declarations: [TaskListPageComponent],
      imports: [TranslateModule.forRoot()],
      providers: [
        {provide: MatDialog, useValue: dialogStub},
        {provide: Overlay, useValue: {scrollStrategies: {reposition: jest.fn().mockReturnValue({})}}},
        {
          provide: BackendConfigurationPnCalendarService,
          useValue: {
            getTasksIndex: jest.fn().mockReturnValue(of({success: true, model: []})),
            getBoards: jest.fn().mockReturnValue(of({success: true, model: []})),
          },
        },
        {
          provide: BackendConfigurationPnPropertiesService,
          useValue: {
            getAllPropertiesDictionary: jest.fn().mockReturnValue(of({success: true, model: []})),
            getDeviceUsersFiltered: jest.fn().mockReturnValue(of({success: true, model: []})),
            getLinkedFolderDtos: jest.fn().mockReturnValue(of({success: true, model: []})),
          },
        },
        {
          provide: ItemsPlanningPnTagsService,
          useValue: {getPlanningsTags: jest.fn().mockReturnValue(of({success: true, model: []}))},
        },
        {
          provide: EFormService,
          useValue: {getAll: jest.fn().mockReturnValue(of({success: true, model: {templates: []}}))},
        },
        {
          provide: BackendConfigurationPnWorkerTagsService,
          useValue: {getWorkerTags: jest.fn().mockReturnValue(of({success: true, model: []}))},
        },
        {provide: CalendarRepeatService, useValue: {}},
        {provide: BackendConfigurationPnTaskListService, useValue: {}},
        {provide: Store, useValue: {select: jest.fn().mockReturnValue(isAdmin$)}},
      ],
      schemas: [NO_ERRORS_SCHEMA],
    }).compileComponents();

    fixture = TestBed.createComponent(TaskListPageComponent);
    component = fixture.componentInstance;
  }

  const q = (selector: string): HTMLElement | null =>
    fixture.nativeElement.querySelector(selector);

  // =========================================================================
  // 1. Role gating — rendered template
  // =========================================================================
  describe('batch UI gating', () => {
    it('admin: batch dropdown rendered, grid selectable, counter shown for a selection', async () => {
      await setup(true);
      fixture.detectChanges();

      expect(component.isAdmin).toBe(true);
      expect(q('#taskListBatchAction')).not.toBeNull();
      // NO_ERRORS_SCHEMA leaves <app-task-list-table> unknown, so the binding
      // lands as a DOM property — assert exactly what the grid is handed.
      expect((q('app-task-list-table') as any).isAdmin).toBe(true);

      component.selection = new Set([1, 2]);
      fixture.detectChanges();
      expect(q('#taskListSelectionCount')).not.toBeNull();
    });

    it('user: no batch dropdown, no selection counter, no checkbox column', async () => {
      await setup(false);
      fixture.detectChanges();

      expect(component.isAdmin).toBe(false);
      expect(q('#taskListBatchAction')).toBeNull();
      expect((q('app-task-list-table') as any).isAdmin).toBe(false);

      // Even with a (stale) selection the counter stays hidden.
      component.selection = new Set([1]);
      fixture.detectChanges();
      expect(q('#taskListSelectionCount')).toBeNull();
    });

    it('user: everything the spec did not mention stays (refresh, Manage tags, CSV export, grid)', async () => {
      await setup(false);
      fixture.detectChanges();

      expect(q('#taskListRefreshBtn')).not.toBeNull();
      expect(q('#taskListManageTagsBtn')).not.toBeNull();
      expect(q('#taskListCsvExportBtn')).not.toBeNull();
      expect(q('app-task-list-table')).not.toBeNull();
      expect(q('app-task-list-filters')).not.toBeNull();
    });

    it('reads the role ONCE — a later emission never flips the grid inputs', async () => {
      await setup(true);
      fixture.detectChanges();

      isAdmin$.next(false);
      fixture.detectChanges();

      expect(component.isAdmin).toBe(true);
      expect(q('#taskListBatchAction')).not.toBeNull();
    });
  });

  // =========================================================================
  // 2. #1140 — the edit modal's date
  // =========================================================================
  describe('openEditTaskModal date (#1140)', () => {
    beforeEach(async () => {
      await setup(true);
      component.ngOnInit();
      // Thursday 17 Sep 2026, 12:00 local.
      jest.useFakeTimers({now: new Date(2026, 8, 17, 12, 0, 0)});
    });

    afterEach(() => jest.useRealTimers());

    const openedData = (): TaskCreateEditModalData => dialogStub.open.mock.calls[0][1].data;

    it.each([
      ['weekly, started 2025', {
        taskDate: '2025-01-06', repeatRule: 'weeklyOne', startHour: 9,
        upcomingOccurrenceDates: ['2026-09-21', '2026-09-28', '2026-10-05'],
      }, '2026-09-21'],
      ['daily, today\'s 09:00 already started', {
        taskDate: '2023-09-17', repeatRule: 'daily', startHour: 9,
        upcomingOccurrenceDates: ['2026-09-16', '2026-09-17', '2026-09-18'],
      }, '2026-09-18'],
      ['daily, today\'s 14:00 still ahead', {
        taskDate: '2023-09-17', repeatRule: 'daily', startHour: 14,
        upcomingOccurrenceDates: ['2026-09-16', '2026-09-17', '2026-09-18'],
      }, '2026-09-17'],
      ['monthly 2nd Tuesday', {
        taskDate: '2026-01-13', repeatRule: 'monthlyDom', startHour: 9,
        upcomingOccurrenceDates: ['2026-10-13', '2026-11-10', '2026-12-08'],
      }, '2026-10-13'],
      ['yearly', {
        taskDate: '2024-03-14', repeatRule: 'yearlyOne', startHour: 9,
        upcomingOccurrenceDates: ['2027-03-14', '2028-03-14', '2029-03-14'],
      }, '2027-03-14'],
      ['custom every 3 days', {
        taskDate: '2026-09-01', repeatRule: 'custom', repeatType: 1, repeatEvery: 3, startHour: 9,
        upcomingOccurrenceDates: ['2026-09-19', '2026-09-22', '2026-09-25'],
      }, '2026-09-19'],
    ])('past-started series (%s) opens on the next upcoming occurrence', (_c, overrides, expected) => {
      const task = buildTask(overrides as Partial<CalendarTaskModel>);
      component.onEditTask(task);

      const data = openedData();
      expect(data.date).toBe(expected);
      expect(data.task!.taskDate).toBe(expected);
      expect(data.task!.id).toBe(ROW_TASK_ID);
      // The grid row itself is not mutated — its Start date column keeps the series start.
      expect(task.taskDate).toBe((overrides as any).taskDate);
    });

    it('a future-start series opens on its series start, unchanged', () => {
      const task = buildTask({taskDate: '2026-10-01', upcomingOccurrenceDates: ['2026-10-05', '2026-10-12']});
      component.onEditTask(task);

      expect(openedData().date).toBe('2026-10-01');
      expect(openedData().task).toBe(task);
    });

    it('a past one-off keeps its own date (no series to advance; opens read-only as before)', () => {
      const task = buildTask({taskDate: '2026-06-09', repeatRule: 'none', repeatType: 0, upcomingOccurrenceDates: null});
      component.onEditTask(task);

      expect(openedData().date).toBe('2026-06-09');
    });

    it('hands the modal the recurring task\'s repeatRule, so its scope dialog applies', () => {
      component.onEditTask(buildTask());

      expect(openedData().task!.repeatRule).toBe('weeklyOne');
    });
  });

  // =========================================================================
  // 3. List → modal, end to end at unit level: with the data the list now
  //    hands over, the REAL modal is editable and its save shows the same
  //    this / this-and-following / all dialog as the calendar. With the old
  //    data (date = series start in the past) the modal was read-only and
  //    onSave() returned before ever reaching the scope dialog.
  // =========================================================================
  describe('the modal opened from the list (#1140 + scope dialog)', () => {
    let modalDialog: {open: jest.Mock};

    function buildModal(data: TaskCreateEditModalData): TaskCreateEditModalComponent {
      modalDialog = {open: jest.fn().mockReturnValue({afterClosed: () => of(undefined)})};
      const modal = new TaskCreateEditModalComponent(
        {close: jest.fn()} as any,
        data,
        {
          getBoards: jest.fn().mockReturnValue(of({success: true, model: [{id: 1, name: 'Kalender', color: '#fff'}]})),
          createTask: jest.fn().mockReturnValue(of({success: false})),
          updateTask: jest.fn().mockReturnValue(of({success: false})),
        } as any,
        {
          buildRepeatSelectOptions: jest.fn().mockReturnValue([]),
          reconstructMetaFromTask: jest.fn().mockReturnValue(null),
          reanchorMetaToDate: jest.fn((m: any) => m),
          metaToWeekdaysCsv: jest.fn().mockReturnValue(null),
          metaToDayOfMonth: jest.fn().mockReturnValue(null),
          metaToRepeatOrdinalWeek: jest.fn().mockReturnValue(null),
        } as any,
        modalDialog as any,
        {scrollStrategies: {reposition: jest.fn().mockReturnValue({})}} as any,
        {instant: (k: string) => k, currentLang: 'da'} as any,
        {getVisualEditorTemplate: jest.fn().mockReturnValue(of({success: false}))} as any,
        {getLinkedSites: jest.fn().mockReturnValue(of({success: true, model: [
          {id: 12, name: 'Clara Holm', description: '', languageId: 1},
        ]}))} as any,
        {select: () => of(1)} as any,
        {error: jest.fn()} as any,
        {createPlanningTag: jest.fn()} as any,
        {} as any,
        {} as any,
        {
          translationPossible: jest.fn().mockReturnValue(of({success: true, model: false})),
          getTranslation: jest.fn(),
        } as any,
        {getLanguages: jest.fn().mockReturnValue(of({success: true, model: {languages: [
          {id: 1, languageCode: 'da', name: 'Dansk', isActive: true},
        ]}}))} as any,
        {getWorkerTags: jest.fn().mockReturnValue(of({success: true, model: []}))} as any,
      );
      modal.ngOnInit();
      return modal;
    }

    beforeEach(async () => {
      await setup(true);
      component.ngOnInit();
      component.boards = [{id: 1, name: 'Kalender', color: '#fff', propertyId: 5} as any];
      jest.useFakeTimers({now: new Date(2026, 8, 17, 12, 0, 0)});
    });

    afterEach(() => jest.useRealTimers());

    it('a past-started recurring series opens EDITABLE and Save shows RepeatScopeModalComponent', () => {
      component.onEditTask(buildTask());
      const modal = buildModal(dialogStub.open.mock.calls[0][1].data);

      expect(modal.isReadonly).toBe(false);

      modal.onSave();

      expect(modalDialog.open).toHaveBeenCalledTimes(1);
      expect(modalDialog.open.mock.calls[0][0]).toBe(RepeatScopeModalComponent);
      expect(modalDialog.open.mock.calls[0][1].data).toEqual({mode: 'edit'});
    });

    it('a past-started one-off stays read-only (nothing to advance to)', () => {
      component.onEditTask(buildTask({
        taskDate: '2026-06-09', repeatRule: 'none', repeatType: 0, upcomingOccurrenceDates: null,
      }));
      const modal = buildModal(dialogStub.open.mock.calls[0][1].data);

      expect(modal.isReadonly).toBe(true);
    });
  });
});
