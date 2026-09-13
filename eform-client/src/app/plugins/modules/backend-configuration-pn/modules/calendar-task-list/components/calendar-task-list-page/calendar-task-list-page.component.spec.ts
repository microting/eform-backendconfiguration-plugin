import {ComponentFixture, TestBed} from '@angular/core/testing';
import {NO_ERRORS_SCHEMA} from '@angular/core';
import {MatDialog} from '@angular/material/dialog';
import {Overlay} from '@angular/cdk/overlay';
import {TranslateModule} from '@ngx-translate/core';
import {of} from 'rxjs';
import {EFormService} from 'src/app/common/services';
import {ItemsPlanningPnTagsService} from 'src/app/plugins/modules/items-planning-pn/services';
import {
  BackendConfigurationPnCalendarService,
  BackendConfigurationPnPropertiesService,
  BackendConfigurationPnWorkerTagsService,
} from '../../../../services';
import {CalendarRepeatService} from '../../../calendar/services/calendar-repeat.service';
import {CalendarTaskListPageComponent} from './calendar-task-list-page.component';
import {CalendarTaskListFiltrationModel, CalendarTaskModel} from '../../../../models/calendar';

describe('CalendarTaskListPageComponent', () => {
  let component: CalendarTaskListPageComponent;
  let fixture: ComponentFixture<CalendarTaskListPageComponent>;

  let calendarServiceStub: any;
  let propertiesServiceStub: any;
  let tagsServiceStub: any;
  let eformServiceStub: any;
  let workerTagsServiceStub: any;
  let dialogStub: any;
  let afterClosed$: any;

  beforeEach(async () => {
    calendarServiceStub = {
      getTasksIndex: jest.fn().mockReturnValue(of({success: true, model: []})),
      getBoards: jest.fn().mockReturnValue(of({success: true, model: []})),
    };
    propertiesServiceStub = {
      getAllPropertiesDictionary: jest.fn().mockReturnValue(of({success: true, model: []})),
      getDeviceUsersFiltered: jest.fn().mockReturnValue(of({success: true, model: []})),
      // #1135 — the page resolves the property's Logbøger folder before opening
      // the edit modal. Nested one level down so the recursive search is exercised.
      getLinkedFolderDtos: jest.fn().mockReturnValue(of({
        success: true,
        model: [{id: 5, name: 'Property root', children: [{id: 77, name: 'Logbøger', children: []}]}],
      })),
    };
    tagsServiceStub = {
      getPlanningsTags: jest.fn().mockReturnValue(of({success: true, model: []})),
    };
    eformServiceStub = {
      getAll: jest.fn().mockReturnValue(of({success: true, model: {templates: []}})),
    };
    workerTagsServiceStub = {
      getWorkerTags: jest.fn().mockReturnValue(of({success: true, model: [{id: 3, name: 'Team A'}]})),
    };
    afterClosed$ = of(false);
    dialogStub = {
      open: jest.fn().mockReturnValue({afterClosed: () => afterClosed$}),
    };

    await TestBed.configureTestingModule({
      declarations: [CalendarTaskListPageComponent],
      imports: [TranslateModule.forRoot()],
      providers: [
        {provide: MatDialog, useValue: dialogStub},
        {
          provide: Overlay,
          // dialogConfigHelper reads overlay.scrollStrategies.reposition().
          useValue: {scrollStrategies: {reposition: jest.fn().mockReturnValue({})}},
        },
        {provide: BackendConfigurationPnCalendarService, useValue: calendarServiceStub},
        {provide: BackendConfigurationPnPropertiesService, useValue: propertiesServiceStub},
        {provide: ItemsPlanningPnTagsService, useValue: tagsServiceStub},
        {provide: EFormService, useValue: eformServiceStub},
        {provide: BackendConfigurationPnWorkerTagsService, useValue: workerTagsServiceStub},
        {provide: CalendarRepeatService, useValue: {}},
      ],
      schemas: [NO_ERRORS_SCHEMA],
    }).compileComponents();

    fixture = TestBed.createComponent(CalendarTaskListPageComponent);
    component = fixture.componentInstance;
    // Trigger ngOnInit; every dependency is stubbed to return of(...) so it
    // completes synchronously without throwing.
    fixture.detectChanges();
  });

  it('should create and load on init', () => {
    expect(component).toBeTruthy();
    expect(propertiesServiceStub.getAllPropertiesDictionary).toHaveBeenCalled();
    expect(tagsServiceStub.getPlanningsTags).toHaveBeenCalled();
    // Worker tags must come from the plugin endpoint (#1213), never the core
    // eForm tag index — the core list also contains eForm/template tags, which
    // resolve to zero recipients when picked in the edit modal.
    expect(workerTagsServiceStub.getWorkerTags).toHaveBeenCalled();
    expect(component.teams).toEqual([{id: 3, name: 'Team A'}]);
    expect(eformServiceStub.getAll).toHaveBeenCalled();
    expect(calendarServiceStub.getTasksIndex).toHaveBeenCalled();
  });

  describe('onFiltersChanged', () => {
    it('reloads tasks through getTasksIndex with the new filters', () => {
      calendarServiceStub.getTasksIndex.mockClear();
      const filters: CalendarTaskListFiltrationModel = {
        propertyIds: [1],
        boardIds: [10],
        eformIds: [],
        assignToIds: [],
        tagIds: [],
        status: true,
        complianceEnabled: null,
        nameFilter: 'abc',
      };

      component.onFiltersChanged(filters);

      expect(calendarServiceStub.getTasksIndex).toHaveBeenCalledTimes(1);
      const calls = calendarServiceStub.getTasksIndex.mock.calls;
      const arg = calls[calls.length - 1][0];
      expect(arg.filters).toEqual(filters);
    });
  });

  describe('onEditTask', () => {
    const buildTask = (propertyId = 1) => ({
      id: 7,
      boardId: 10,
      propertyId,
      taskDate: '2026-06-09',
      startHour: 9,
      workerNames: [],
      tags: [],
    } as unknown as CalendarTaskModel);

    it('opens the edit modal via dialog.open', () => {
      component.onEditTask(buildTask());

      expect(dialogStub.open).toHaveBeenCalledTimes(1);
    });

    // #1135 — a null folderId made the server throw on EVERY save from this
    // page ("Nullable object must have a value"), so the modal never closed.
    it('passes the property\'s Logbøger folder id to the modal', () => {
      component.onEditTask(buildTask(1));

      expect(propertiesServiceStub.getLinkedFolderDtos).toHaveBeenCalledWith(1);
      const data = dialogStub.open.mock.calls[0][1].data;
      expect(data.folderId).toBe(77);
    });

    it('resolves the folder once per property and reuses it', () => {
      component.onEditTask(buildTask(1));
      component.onEditTask(buildTask(1));

      expect(propertiesServiceStub.getLinkedFolderDtos).toHaveBeenCalledTimes(1);
      expect(dialogStub.open.mock.calls[1][1].data.folderId).toBe(77);
    });

    it('re-resolves for a different property, because the grid spans properties', () => {
      propertiesServiceStub.getLinkedFolderDtos.mockImplementation((propertyId: number) => of({
        success: true,
        model: [{id: 100 + propertyId, name: 'Logbøger', children: []}],
      }));

      component.onEditTask(buildTask(1));
      component.onEditTask(buildTask(2));

      expect(dialogStub.open.mock.calls[0][1].data.folderId).toBe(101);
      expect(dialogStub.open.mock.calls[1][1].data.folderId).toBe(102);
    });

    // Never another property's folder: that would refile the task under a
    // property it does not belong to (#1239). null is the supported "no folder
    // supplied" value — the backend keeps the task's current folder.
    it('passes null when the folder lookup fails, and retries on the next edit', () => {
      propertiesServiceStub.getLinkedFolderDtos.mockReturnValueOnce(of({success: false, message: 'boom'}));

      component.onEditTask(buildTask(1));
      expect(dialogStub.open.mock.calls[0][1].data.folderId).toBeNull();

      component.onEditTask(buildTask(1));
      expect(propertiesServiceStub.getLinkedFolderDtos).toHaveBeenCalledTimes(2);
      expect(dialogStub.open.mock.calls[1][1].data.folderId).toBe(77);
    });

    it('passes null without a lookup when the task has no property', () => {
      propertiesServiceStub.getLinkedFolderDtos.mockClear();

      component.onEditTask(buildTask(0));

      expect(propertiesServiceStub.getLinkedFolderDtos).not.toHaveBeenCalled();
      expect(dialogStub.open.mock.calls[0][1].data.folderId).toBeNull();
    });
  });
});
