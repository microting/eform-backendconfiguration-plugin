import {of} from 'rxjs';
import {TaskCreateEditModalComponent, TaskCreateEditModalData} from './task-create-edit-modal.component';

/**
 * eform-angular-frontend#8141 — SDK Languages ids are AUTO_INCREMENT values, so on a MariaDB
 * Galera cluster (auto_increment_increment=3) a fresh install gets Dansk=3, English=6. The
 * eForm preview must not assume Danish is language id 1. (Field types arrive from the host
 * API already normalised to EformFieldTypesEnum, so they are not tested here.)
 *
 * Constructed directly with plain stubs, exactly like the #1295 spec next door.
 */

const DANISH = 3;
const ENGLISH = 6;
const LANGUAGES = [
  {id: DANISH, languageCode: 'da', name: 'Dansk', isActive: true},
  {id: ENGLISH, languageCode: 'en-US', name: 'English', isActive: true},
];
function futureDate(days = 7): string {
  const d = new Date();
  d.setDate(d.getDate() + days);
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
}

describe('TaskCreateEditModalComponent — shifted lookup ids (#8141)', () => {
  function build(opts: {
    template: any;
    currentUserLanguageId?: number;
  }): TaskCreateEditModalComponent {
    const calendarService: any = {
      getBoards: jest.fn().mockReturnValue(of({success: true, model: []})),
      createTask: jest.fn().mockReturnValue(of({success: false})),
      updateTask: jest.fn().mockReturnValue(of({success: false})),
    };
    const propertiesService: any = {getLinkedSites: jest.fn().mockReturnValue(of({success: true, model: []}))};
    const workerTagsService: any = {getWorkerTags: jest.fn().mockReturnValue(of({success: true, model: []}))};
    const repeatService: any = {
      buildRepeatSelectOptions: jest.fn().mockReturnValue([]),
      reconstructMetaFromTask: jest.fn().mockReturnValue(null),
      reanchorMetaToDate: jest.fn((m: any) => m),
      metaToWeekdaysCsv: jest.fn().mockReturnValue(null),
      metaToDayOfMonth: jest.fn().mockReturnValue(null),
      metaToRepeatOrdinalWeek: jest.fn().mockReturnValue(null),
    };
    const translate: any = {instant: (k: string) => k, currentLang: 'da'};
    const store: any = {select: () => of(opts.currentUserLanguageId)};
    const eformVisualEditorService: any = {
      getVisualEditorTemplate: jest.fn().mockReturnValue(of({success: true, model: opts.template})),
    };
    const translationService: any = {
      translationPossible: jest.fn().mockReturnValue(of({success: true, model: false})),
      getTranslation: jest.fn(),
    };
    const appSettingsStateService: any = {
      getLanguages: jest.fn().mockReturnValue(of({success: true, model: {languages: LANGUAGES}})),
    };
    const data: TaskCreateEditModalData = {
      task: null,
      date: futureDate(),
      startHour: 9,
      boards: [],
      employees: [],
      tags: [],
      workerTags: [],
      propertyId: 5,
      properties: [],
      eforms: of([]),
      folderId: null,
      planningTags: [],
    };

    const c = new TaskCreateEditModalComponent(
      {close: jest.fn()} as any,
      data,
      calendarService,
      repeatService,
      {open: jest.fn()} as any,
      {} as any,
      translate,
      eformVisualEditorService,
      propertiesService,
      store,
      {error: jest.fn(), warning: jest.fn()} as any,
      {createPlanningTag: jest.fn()} as any,
      {} as any,
      {} as any,
      translationService,
      appSettingsStateService,
      workerTagsService,
    );
    c.ngOnInit();
    c.loadTemplate(42);
    return c;
  }

  it('without a current-user language, names the template in DANISH (real SDK id), not language id 1', () => {
    const c = build({
      currentUserLanguageId: undefined,
      template: {
        // Id 1 exists on this tenant but is another language (e.g. Norsk).
        translations: [{languageId: 1, name: 'Kontroll (no)'}, {languageId: DANISH, name: 'Kontrol (da)'}],
        fields: [],
        checkLists: [],
      },
    });

    expect(c.selectedTemplateLabel).toBe('Kontrol (da)');
  });
});
