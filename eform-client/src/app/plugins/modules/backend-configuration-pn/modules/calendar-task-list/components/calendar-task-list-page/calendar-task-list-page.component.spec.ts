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
    it('opens the edit modal via dialog.open', () => {
      const task = {
        id: 7,
        boardId: 10,
        propertyId: 1,
        taskDate: '2026-06-09',
        startHour: 9,
        workerNames: [],
        tags: [],
      } as unknown as CalendarTaskModel;

      component.onEditTask(task);

      expect(dialogStub.open).toHaveBeenCalledTimes(1);
    });
  });
});
