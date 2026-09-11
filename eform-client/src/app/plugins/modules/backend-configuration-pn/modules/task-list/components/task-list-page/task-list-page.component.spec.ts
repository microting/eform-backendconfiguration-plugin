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
  BackendConfigurationPnTaskListService,
  BackendConfigurationPnWorkerTagsService,
} from '../../../../services';
import {CalendarRepeatService} from '../../../calendar/services/calendar-repeat.service';
import {TaskListPageComponent} from './task-list-page.component';
import {CalendarTaskModel} from '../../../../models/calendar';

/**
 * #1135 — the edit modal's `folderId`.
 *
 * The page hard-coded `folderId: null`, which made the server throw
 * ("Nullable object must have a value") on EVERY save from this page,
 * whatever field had been edited: the dialog never closed and the user got a
 * generic error toast. The same edit from the Calendar page worked, because
 * `CalendarContainerComponent` resolves and passes the property's Logbøger
 * folder — which is what this page now does too.
 *
 * The fixture deliberately calls `ngOnInit()` instead of `detectChanges()`:
 * nothing here asserts on rendered markup, and skipping the render keeps the
 * spec independent of the (large) page template.
 */
describe('TaskListPageComponent — Logbøger folder resolution', () => {
  let component: TaskListPageComponent;
  let fixture: ComponentFixture<TaskListPageComponent>;

  let propertiesServiceStub: any;
  let dialogStub: any;

  const buildTask = (propertyId = 1) => ({
    id: 7,
    boardId: 10,
    propertyId,
    taskDate: '2026-06-09',
    startHour: 9,
    workerNames: [],
    tags: [],
  } as unknown as CalendarTaskModel);

  beforeEach(async () => {
    propertiesServiceStub = {
      getAllPropertiesDictionary: jest.fn().mockReturnValue(of({success: true, model: []})),
      getDeviceUsersFiltered: jest.fn().mockReturnValue(of({success: true, model: []})),
      // Nested one level down so the recursive search is exercised.
      getLinkedFolderDtos: jest.fn().mockReturnValue(of({
        success: true,
        model: [{id: 5, name: 'Property root', children: [{id: 77, name: 'Logbøger', children: []}]}],
      })),
    };
    dialogStub = {
      open: jest.fn().mockReturnValue({afterClosed: () => of(false)}),
    };

    await TestBed.configureTestingModule({
      declarations: [TaskListPageComponent],
      imports: [TranslateModule.forRoot()],
      providers: [
        {provide: MatDialog, useValue: dialogStub},
        {
          provide: Overlay,
          // dialogConfigHelper reads overlay.scrollStrategies.reposition().
          useValue: {scrollStrategies: {reposition: jest.fn().mockReturnValue({})}},
        },
        {
          provide: BackendConfigurationPnCalendarService,
          useValue: {
            getTasksIndex: jest.fn().mockReturnValue(of({success: true, model: []})),
            getBoards: jest.fn().mockReturnValue(of({success: true, model: []})),
          },
        },
        {provide: BackendConfigurationPnPropertiesService, useValue: propertiesServiceStub},
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
      ],
      schemas: [NO_ERRORS_SCHEMA],
    }).compileComponents();

    fixture = TestBed.createComponent(TaskListPageComponent);
    component = fixture.componentInstance;
    component.ngOnInit();
  });

  it('passes the property\'s Logbøger folder id to the edit modal', () => {
    component.onEditTask(buildTask(1));

    expect(propertiesServiceStub.getLinkedFolderDtos).toHaveBeenCalledWith(1);
    expect(dialogStub.open).toHaveBeenCalledTimes(1);
    expect(dialogStub.open.mock.calls[0][1].data.folderId).toBe(77);
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
