import {of, throwError, Subject} from 'rxjs';
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
  let translationService: any;
  let toastr: any;
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
    toastr = {error: jest.fn(), warning: jest.fn()};
    const translate: any = {instant: (k: string) => k, currentLang: 'da'};
    const store: any = {select: () => of(1)};
    const eformVisualEditorService: any = {getVisualEditorTemplate: jest.fn().mockReturnValue(of({success: false}))};
    translationService = {
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
      toastr,
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
    it('splits the merged selection into sites / workerTagIds (no DTO change)', async () => {
      component = build();
      component.titleControl.setValue('New task');
      component.assigneeControl.setValue(['t:7', 's:11']);

      await component.onSave();

      expect(calendarService.createTask).toHaveBeenCalledTimes(1);
      const payload = calendarService.createTask.mock.calls[0][0];
      expect(payload.sites).toEqual([11]);
      expect(payload.workerTagIds).toEqual([7]);
      expect(payload.assigneeIds).toEqual([11]);
    });

    it('sends a team-only selection as workerTagIds with empty sites', async () => {
      component = build();
      component.titleControl.setValue('New task');
      component.assigneeControl.setValue(['t:8']);

      await component.onSave();

      const payload = calendarService.createTask.mock.calls[0][0];
      expect(payload.sites).toEqual([]);
      expect(payload.workerTagIds).toEqual([8]);
    });
  });

  describe('onSave fills missing translations (#1324)', () => {
    const WARNING = 'Automatic translation was not possible. The task is saved without it.';

    /** Danish title + description, assigned to an English (12) and a German (13) worker. */
    function buildForSave(): TaskCreateEditModalComponent {
      const c = build();
      c.titleControl.setValue('Tjek ventilation');
      c.descriptionControl.setValue('Rengør filtre');
      c.assigneeControl.setValue(['s:12', 's:13']);
      return c;
    }

    /**
     * Translation configured; each call echoes its target code. The init probe has
     * already answered "not configured" when build() ran, so the component's cached
     * answer is set directly (onSave only re-probes when the probe never answered).
     */
    function configureTranslation() {
      translationService.translationPossible.mockReturnValue(of({success: true, model: true}));
      (component as any).translationConfigured = true;
      translationService.getTranslation.mockImplementation((req: any) =>
        of({success: true, model: `[${req.targetLanguageCode}] ${req.sourceText}`}));
    }

    const savedTranslates = () => calendarService.createTask.mock.calls[0][0].translates as
      {name: string; description: string; languageId: number}[];

    it('saves nothing when the dialog is closed while translations are fetched', async () => {
      component = buildForSave();
      configureTranslation();
      const pending = new Subject<any>();
      translationService.getTranslation.mockReturnValue(pending);

      const save = component.onSave();
      component.ngOnDestroy();
      pending.next({success: true, model: '[x] late'});
      pending.complete();
      await save;

      expect(calendarService.createTask).not.toHaveBeenCalled();
    });

    it('fills every empty target title and description before saving', async () => {
      component = buildForSave();
      configureTranslation();

      await component.onSave();

      expect(component.titleByLang[ENGLISH]).toBe('[en-US] Tjek ventilation');
      expect(component.descByLang[ENGLISH]).toBe('[en-US] Rengør filtre');
      expect(component.titleByLang[GERMAN]).toBe('[de-DE] Tjek ventilation');
      expect(component.descByLang[GERMAN]).toBe('[de-DE] Rengør filtre');
      expect(savedTranslates()).toEqual([
        {name: 'Tjek ventilation', description: 'Rengør filtre', languageId: DANISH},
        {name: '[en-US] Tjek ventilation', description: '[en-US] Rengør filtre', languageId: ENGLISH},
        {name: '[de-DE] Tjek ventilation', description: '[de-DE] Rengør filtre', languageId: GERMAN},
      ]);
      expect(toastr.warning).not.toHaveBeenCalled();
    });

    it('never overwrites a target that already has text', async () => {
      component = buildForSave();
      configureTranslation();
      component.titleByLang[ENGLISH] = 'Check ventilation';

      await component.onSave();

      expect(component.titleByLang[ENGLISH]).toBe('Check ventilation');
      expect(translationService.getTranslation).not.toHaveBeenCalledWith(
        expect.objectContaining({targetLanguageCode: 'en-US', sourceText: 'Tjek ventilation'}));
      expect(savedTranslates()).toContainEqual(
        {name: 'Check ventilation', description: '[en-US] Rengør filtre', languageId: ENGLISH});
    });

    it('keeps text typed into a target while its translation was running', async () => {
      component = buildForSave();
      configureTranslation();
      translationService.getTranslation.mockImplementation((req: any) => {
        if (req.targetLanguageCode === 'en-US' && req.sourceText === 'Tjek ventilation') {
          component.titleByLang[ENGLISH] = 'Typed meanwhile';
        }
        return of({success: true, model: `[${req.targetLanguageCode}] ${req.sourceText}`});
      });

      await component.onSave();

      expect(component.titleByLang[ENGLISH]).toBe('Typed meanwhile');
    });

    it('saves with only the non-empty entries and warns when translation is not configured', async () => {
      component = buildForSave();
      component.titleByLang[ENGLISH] = 'Check ventilation';

      await component.onSave();

      expect(translationService.getTranslation).not.toHaveBeenCalled();
      expect(toastr.warning).toHaveBeenCalledWith(WARNING);
      expect(calendarService.createTask).toHaveBeenCalledTimes(1);
      expect(savedTranslates()).toEqual([
        {name: 'Tjek ventilation', description: 'Rengør filtre', languageId: DANISH},
        {name: 'Check ventilation', description: '', languageId: ENGLISH},
      ]);
    });

    it('warns on a failed call but still fills and saves the others', async () => {
      component = buildForSave();
      configureTranslation();
      translationService.getTranslation.mockImplementation((req: any) =>
        req.targetLanguageCode === 'de-DE' && req.sourceText === 'Tjek ventilation'
          ? throwError(() => new Error('translation service down'))
          : of({success: true, model: `[${req.targetLanguageCode}] ${req.sourceText}`}));

      await component.onSave();

      expect(toastr.warning).toHaveBeenCalledWith(WARNING);
      expect(calendarService.createTask).toHaveBeenCalledTimes(1);
      expect(savedTranslates()).toEqual([
        {name: 'Tjek ventilation', description: 'Rengør filtre', languageId: DANISH},
        {name: '[en-US] Tjek ventilation', description: '[en-US] Rengør filtre', languageId: ENGLISH},
        {name: '', description: '[de-DE] Rengør filtre', languageId: GERMAN},
      ]);
    });

    it('ignores a second Save click while translations are being filled', async () => {
      component = buildForSave();
      configureTranslation();

      const first = component.onSave();
      const second = component.onSave();
      await Promise.all([first, second]);

      expect(calendarService.createTask).toHaveBeenCalledTimes(1);
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
