import {of} from 'rxjs';
import {
  AssigneeOption,
  siteKey,
  splitAssigneeKeys,
  TaskCreateEditModalComponent,
  TaskCreateEditModalData,
  teamKey,
} from './task-create-edit-modal.component';
import {CalendarTaskModel} from '../../../../models/calendar';

/**
 * #1295 — the merged "Vælg medarbejder / team" picker.
 *
 * Teams (worker tags) and workers (sites) share ONE control with string keys
 * ('t:'+tagId / 's:'+siteId); the team list is property-scoped and reloads on
 * property change; `onSave` splits the keys back into the unchanged wire fields
 * `workerTagIds` / `sites`.
 *
 * The component is constructed directly (no TestBed/template): every collaborator
 * is a plain stub, so these tests exercise the class logic the template binds to.
 */

// Property 5: workers 11 (Danish), 12 (English), 13 (German); team 7 has members
// 12 and 13 on this property, team 8 has member 11.
const DANISH = 1;
const ENGLISH = 2;
const GERMAN = 3;
const WORKERS_P5 = [
  {id: 11, name: 'Anders Jensen', description: '', languageId: DANISH},
  {id: 12, name: 'Clara Holm', description: '', languageId: ENGLISH},
  {id: 13, name: 'Emma Nielsen', description: '', languageId: GERMAN},
];
const TEAMS_P5 = [
  {id: 7, name: 'Service team', description: '', memberSiteIds: [12, 13]},
  {id: 8, name: 'Stald team', description: '', memberSiteIds: [11]},
];
// Property 6: one worker, no teams.
const WORKERS_P6 = [{id: 21, name: 'Peter Hansen', description: '', languageId: DANISH}];
const TEAMS_P6: typeof TEAMS_P5 = [];

const LANGUAGES = [
  {id: DANISH, languageCode: 'da', name: 'Dansk', isActive: true},
  {id: ENGLISH, languageCode: 'en-US', name: 'English', isActive: true},
  {id: GERMAN, languageCode: 'de-DE', name: 'Deutsch', isActive: true},
];

function futureDate(days = 7): string {
  const d = new Date();
  d.setDate(d.getDate() + days);
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
}

function pastDate(days = 7): string {
  return futureDate(-days);
}

function makeTask(overrides: Partial<CalendarTaskModel> = {}): CalendarTaskModel {
  return {
    id: 99,
    title: 'Existing',
    startHour: 9,
    duration: 1,
    startText: '09:00',
    endText: '10:00',
    tags: [],
    assigneeIds: [12],
    workerNames: ['Clara Holm'],
    workerTagIds: [7],
    workerTagNames: ['Service team'],
    boardId: 1,
    color: '#fff',
    descriptionHtml: '',
    repeatRule: 'none',
    taskDate: futureDate(),
    completed: false,
    status: true,
    complianceEnabled: true,
    propertyId: 5,
    ...overrides,
  } as CalendarTaskModel;
}

describe('TaskCreateEditModalComponent — merged teams/workers picker (#1295)', () => {
  let calendarService: any;
  let propertiesService: any;
  let workerTagsService: any;
  let component: TaskCreateEditModalComponent;

  function build(data: Partial<TaskCreateEditModalData> = {}): TaskCreateEditModalComponent {
    calendarService = {
      getBoards: jest.fn().mockReturnValue(of({success: true, model: [{id: 1, name: 'Kalender', color: '#fff'}]})),
      createTask: jest.fn().mockReturnValue(of({success: false})),
      updateTask: jest.fn().mockReturnValue(of({success: false})),
    };
    propertiesService = {
      getLinkedSites: jest.fn((propertyId: number) =>
        of({success: true, model: propertyId === 5 ? WORKERS_P5 : WORKERS_P6})),
    };
    workerTagsService = {
      getWorkerTags: jest.fn((propertyId: number) =>
        of({success: true, model: propertyId === 5 ? TEAMS_P5 : TEAMS_P6})),
    };
    const repeatService: any = {
      buildRepeatSelectOptions: jest.fn().mockReturnValue([]),
      reconstructMetaFromTask: jest.fn().mockReturnValue(null),
      reanchorMetaToDate: jest.fn((m: any) => m),
      metaToWeekdaysCsv: jest.fn().mockReturnValue(null),
      metaToDayOfMonth: jest.fn().mockReturnValue(null),
      metaToRepeatOrdinalWeek: jest.fn().mockReturnValue(null),
    };
    const translate: any = {instant: (k: string) => k, currentLang: 'da'};
    const store: any = {select: () => of(1)};
    const eformVisualEditorService: any = {getVisualEditorTemplate: jest.fn().mockReturnValue(of({success: false}))};
    const translationService: any = {
      translationPossible: jest.fn().mockReturnValue(of({success: true, model: false})),
      getTranslation: jest.fn(),
    };
    const appSettingsStateService: any = {
      getLanguages: jest.fn().mockReturnValue(of({success: true, model: {languages: LANGUAGES}})),
    };

    const fullData: TaskCreateEditModalData = {
      task: null,
      date: futureDate(),
      startHour: 9,
      boards: [{id: 1, name: 'Kalender', color: '#fff'} as any],
      employees: [],
      tags: [],
      workerTags: [{id: 7, name: 'Service team'}, {id: 8, name: 'Stald team'}, {id: 30, name: 'Other-property team'}] as any,
      propertyId: 5,
      properties: [],
      eforms: of([]),
      folderId: null,
      planningTags: [],
      ...data,
    };

    const c = new TaskCreateEditModalComponent(
      {close: jest.fn()} as any,
      fullData,
      calendarService,
      repeatService,
      {open: jest.fn()} as any,
      {} as any,
      translate,
      eformVisualEditorService,
      propertiesService,
      store,
      {error: jest.fn()} as any,
      {createPlanningTag: jest.fn()} as any,
      {} as any,
      {} as any,
      translationService,
      appSettingsStateService,
      workerTagsService,
    );
    c.ngOnInit();
    return c;
  }

  const groupsInOrder = (items: AssigneeOption[]) =>
    items.map(i => i.group).filter((g, i, arr) => arr.indexOf(g) === i);

  describe('key helpers', () => {
    it('splits team and site keys back into separate id lists', () => {
      expect(splitAssigneeKeys(['t:7', 's:12', 's:13', 't:8'])).toEqual({siteIds: [12, 13], workerTagIds: [7, 8]});
    });

    it('keeps equal numeric ids apart — tag ids and site ids are separate id spaces', () => {
      expect(splitAssigneeKeys([teamKey(5), siteKey(5)])).toEqual({siteIds: [5], workerTagIds: [5]});
    });

    it('treats null/undefined as empty', () => {
      expect(splitAssigneeKeys(null)).toEqual({siteIds: [], workerTagIds: []});
      expect(splitAssigneeKeys(undefined)).toEqual({siteIds: [], workerTagIds: []});
    });
  });

  describe('items', () => {
    it('lists the Teams group FIRST and workers LAST, with t:/s: keys', () => {
      component = build();

      expect(groupsInOrder(component.assigneeItems)).toEqual(['teams', 'workers']);
      expect(component.assigneeItems.map(i => i.key)).toEqual(['t:7', 't:8', 's:11', 's:12', 's:13']);
      expect(component.assigneeItems.filter(i => i.group === 'teams').map(i => i.name))
        .toEqual(['Service team', 'Stald team']);
    });

    it('has NO Teams group when the property has no teams', () => {
      component = build({propertyId: 6});

      expect(groupsInOrder(component.assigneeItems)).toEqual(['workers']);
      expect(component.assigneeItems.map(i => i.key)).toEqual(['s:21']);
    });

    it('loads the teams for the SELECTED property, not installation-wide', () => {
      component = build();

      expect(workerTagsService.getWorkerTags).toHaveBeenCalledWith(5);
      // The installation-wide data.workerTags (which contains team 30) is not offered.
      expect(component.assigneeItems.some(i => i.key === 't:30')).toBe(false);
    });
  });

  describe('onSave', () => {
    it('splits the merged selection into sites / workerTagIds (no DTO change)', () => {
      component = build();
      component.titleControl.setValue('New task');
      component.assigneeControl.setValue(['t:7', 's:11']);

      component.onSave();

      expect(calendarService.createTask).toHaveBeenCalledTimes(1);
      const payload = calendarService.createTask.mock.calls[0][0];
      expect(payload.sites).toEqual([11]);
      expect(payload.workerTagIds).toEqual([7]);
      expect(payload.assigneeIds).toEqual([11]);
    });

    it('sends a team-only selection as workerTagIds with empty sites', () => {
      component = build();
      component.titleControl.setValue('New task');
      component.assigneeControl.setValue(['t:8']);

      component.onSave();

      const payload = calendarService.createTask.mock.calls[0][0];
      expect(payload.sites).toEqual([]);
      expect(payload.workerTagIds).toEqual([8]);
    });
  });

  describe('Save gate', () => {
    it('is closed with nothing picked', () => {
      component = build();
      expect(component.hasAssignee).toBe(false);
    });

    it('opens with only a team picked', () => {
      component = build();
      component.assigneeControl.setValue(['t:7']);
      expect(component.hasAssignee).toBe(true);
    });

    it('opens with only a worker picked', () => {
      component = build();
      component.assigneeControl.setValue(['s:12']);
      expect(component.hasAssignee).toBe(true);
    });
  });

  describe('edit / copy seeding', () => {
    it('edit mode seeds teams from task.workerTagIds and workers from task.assigneeIds', () => {
      component = build({task: makeTask()});

      expect(component.assigneeControl.value).toEqual(['t:7', 's:12']);
    });

    it('copy mode seeds from the source task the same way', () => {
      component = build({sourceTask: makeTask({workerTagIds: [8], assigneeIds: [11, 13]})});

      expect(component.assigneeControl.value).toEqual(['t:8', 's:11', 's:13']);
    });

    it('a seeded team the property no longer offers stays visible as a named chip and is posted back', () => {
      component = build({task: makeTask({workerTagIds: [30], workerTagNames: ['Other-property team']})});

      const retained = component.assigneeItems.find(i => i.key === 't:30');
      expect(retained).toBeDefined();
      expect(retained!.name).toBe('Other-property team');
      expect(retained!.group).toBe('teams');
      expect(retained!.retained).toBe(true);
    });

    it('a retained team disappears from the list once deselected', () => {
      component = build({task: makeTask({workerTagIds: [30], workerTagNames: ['Other-property team']})});

      component.assigneeControl.setValue(['s:12']);

      expect(component.assigneeItems.some(i => i.key === 't:30')).toBe(false);
    });
  });

  describe('property change', () => {
    it('clears BOTH teams and workers and reloads both lists for the new property', () => {
      component = build();
      component.assigneeControl.setValue(['t:7', 's:12']);

      component.propertyControl.setValue(6);

      expect(component.assigneeControl.value).toEqual([]);
      expect(workerTagsService.getWorkerTags).toHaveBeenLastCalledWith(6);
      expect(propertiesService.getLinkedSites).toHaveBeenLastCalledWith(6, false);
      expect(component.assigneeItems.map(i => i.key)).toEqual(['s:21']);
    });
  });

  describe('target languages', () => {
    it('include the languages of the picked team\'s members', () => {
      component = build();

      component.assigneeControl.setValue(['t:7']);

      expect(component.targetLanguages.map(l => l.id).sort()).toEqual([ENGLISH, GERMAN]);
    });

    it('union a team\'s members with directly picked workers, Danish excluded', () => {
      component = build();

      component.assigneeControl.setValue(['t:8', 's:12']);

      // Team 8's only member (11) is Danish; the direct pick adds English.
      expect(component.targetLanguages.map(l => l.id)).toEqual([ENGLISH]);
    });

    it('are empty with only a Danish-speaking team', () => {
      component = build();

      component.assigneeControl.setValue(['t:8']);

      expect(component.targetLanguages).toEqual([]);
    });
  });

  describe('readonly', () => {
    it('disables the merged picker for a past task', () => {
      component = build({task: makeTask({taskDate: pastDate()})});

      expect(component.isReadonly).toBe(true);
      expect(component.assigneeControl.disabled).toBe(true);
    });

    it('disables the merged picker for a completed task', () => {
      component = build({task: makeTask({completed: true})});

      expect(component.isReadonly).toBe(true);
      expect(component.assigneeControl.disabled).toBe(true);
    });

    it('keeps the seeded team + worker selection visible while readonly', () => {
      component = build({task: makeTask({taskDate: pastDate()})});

      expect(component.assigneeControl.value).toEqual(['t:7', 's:12']);
    });
  });
});
