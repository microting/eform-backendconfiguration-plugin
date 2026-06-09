import {ComponentFixture, TestBed} from '@angular/core/testing';
import {NO_ERRORS_SCHEMA} from '@angular/core';
import {TranslateModule} from '@ngx-translate/core';
import {CalendarTaskListTableComponent} from './calendar-task-list-table.component';
import {CalendarRepeatService} from '../../../calendar/services/calendar-repeat.service';
import {CalendarBoardModel, CalendarTaskModel} from '../../../../models/calendar';
import {CommonDictionaryModel, SharedTagModel} from 'src/app/common/models';

describe('CalendarTaskListTableComponent', () => {
  let component: CalendarTaskListTableComponent;
  let fixture: ComponentFixture<CalendarTaskListTableComponent>;
  let repeatServiceStub: {
    reconstructMetaFromTask: jest.Mock;
    formatCustomRepeatLabel: jest.Mock;
  };

  const properties: CommonDictionaryModel[] = [
    {id: 1, name: 'Property One', description: ''},
    {id: 2, name: 'Property Two', description: ''},
  ];
  const boards: CalendarBoardModel[] = [
    {id: 10, name: 'Board Ten', color: '#d04d4d', propertyId: 1},
    {id: 20, name: 'Board Twenty', color: '#5ba8ff', propertyId: 2},
  ];
  const planningTags: SharedTagModel[] = [{id: 5, name: 'Tag Five'} as SharedTagModel];

  function makeTask(overrides: Partial<CalendarTaskModel> = {}): CalendarTaskModel {
    return {
      id: 1,
      title: 'Task',
      startHour: 9,
      duration: 1,
      startText: '09:00',
      endText: '10:00',
      tags: [],
      assigneeIds: [],
      workerNames: [],
      boardId: 10,
      color: '',
      descriptionHtml: '',
      repeatRule: 'none',
      taskDate: '2026-06-09',
      completed: false,
      status: true,
      complianceEnabled: false,
      propertyId: 1,
      ...overrides,
    } as CalendarTaskModel;
  }

  beforeEach(async () => {
    repeatServiceStub = {
      reconstructMetaFromTask: jest.fn(),
      // Ignore the locale arg so getCurrentLocale(translate) can't affect output.
      formatCustomRepeatLabel: jest.fn().mockReturnValue('Ugentligt hver mandag'),
    };

    await TestBed.configureTestingModule({
      declarations: [CalendarTaskListTableComponent],
      imports: [TranslateModule.forRoot()],
      providers: [{provide: CalendarRepeatService, useValue: repeatServiceStub}],
      schemas: [NO_ERRORS_SCHEMA],
    }).compileComponents();

    fixture = TestBed.createComponent(CalendarTaskListTableComponent);
    component = fixture.componentInstance;
    component.properties = properties;
    component.boards = boards;
    component.planningTags = planningTags;
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  describe('propertyName', () => {
    it('resolves a name from the @Input properties list', () => {
      expect(component.propertyName(1)).toBe('Property One');
      expect(component.propertyName(2)).toBe('Property Two');
    });

    it('returns empty string for unknown or nullish ids', () => {
      expect(component.propertyName(999)).toBe('');
      expect(component.propertyName(null)).toBe('');
      expect(component.propertyName(undefined)).toBe('');
    });
  });

  describe('board', () => {
    it('resolves a board from the @Input boards list', () => {
      expect(component.board(10)).toEqual(boards[0]);
      expect(component.board(20)).toEqual(boards[1]);
    });

    it('returns undefined for unknown or nullish ids', () => {
      expect(component.board(999)).toBeUndefined();
      expect(component.board(null)).toBeUndefined();
      expect(component.board(undefined)).toBeUndefined();
    });
  });

  describe('repeatText', () => {
    it('returns the stubbed humanized label for a repeating task', () => {
      const meta = {kind: 'weeklyOne', weekday: 1, endMode: 'never'};
      repeatServiceStub.reconstructMetaFromTask.mockReturnValue(meta);
      const task = makeTask({repeatRule: 'custom'});

      const result = component.repeatText(task);

      expect(repeatServiceStub.reconstructMetaFromTask).toHaveBeenCalled();
      expect(repeatServiceStub.formatCustomRepeatLabel).toHaveBeenCalled();
      expect(result).toBe('Ugentligt hver mandag');
    });

    it("returns the 'Does not repeat' key for repeatRule 'none'", () => {
      // TranslateModule.forRoot() echoes the key when no translation is loaded.
      const result = component.repeatText(makeTask({repeatRule: 'none'}));
      expect(result).toBe('Does not repeat');
      expect(repeatServiceStub.reconstructMetaFromTask).not.toHaveBeenCalled();
    });
  });

  describe('onEdit', () => {
    it('emits editTask on row click', () => {
      const task = makeTask({id: 42});
      const emitted: CalendarTaskModel[] = [];
      component.editTask.subscribe((t: CalendarTaskModel) => emitted.push(t));

      component.onEdit(task);

      expect(emitted.length).toBe(1);
      expect(emitted[0].id).toBe(42);
    });
  });
});
