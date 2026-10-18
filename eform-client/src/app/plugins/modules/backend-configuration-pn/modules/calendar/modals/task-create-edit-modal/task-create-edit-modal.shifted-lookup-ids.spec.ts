import {describe, it, expect, vi} from 'vitest';
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
      getBoards: vi.fn().mockReturnValue(of({success: true, model: []})),
      createTask: vi.fn().mockReturnValue(of({success: false})),
      updateTask: vi.fn().mockReturnValue(of({success: false})),
    };
    const propertiesService: any = {getLinkedSites: vi.fn().mockReturnValue(of({success: true, model: []}))};
    const workerTagsService: any = {getWorkerTags: vi.fn().mockReturnValue(of({success: true, model: []}))};
    const repeatService: any = {
      buildRepeatSelectOptions: vi.fn().mockReturnValue([]),
      reconstructMetaFromTask: vi.fn().mockReturnValue(null),
      reanchorMetaToDate: vi.fn((m: any) => m),
      metaToWeekdaysCsv: vi.fn().mockReturnValue(null),
      metaToDayOfMonth: vi.fn().mockReturnValue(null),
      metaToRepeatOrdinalWeek: vi.fn().mockReturnValue(null),
    };
    const translate: any = {instant: (k: string) => k, currentLang: 'da'};
    const store: any = {select: () => of(opts.currentUserLanguageId)};
    const eformVisualEditorService: any = {
      getVisualEditorTemplate: vi.fn().mockReturnValue(of({success: true, model: opts.template})),
    };
    const translationService: any = {
      translationPossible: vi.fn().mockReturnValue(of({success: true, model: false})),
      getTranslation: vi.fn(),
    };
    const appSettingsStateService: any = {
      getLanguages: vi.fn().mockReturnValue(of({success: true, model: {languages: LANGUAGES}})),
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
      {close: vi.fn()} as any,
      data,
      calendarService,
      repeatService,
      {open: vi.fn()} as any,
      {} as any,
      translate,
      eformVisualEditorService,
      propertiesService,
      store,
      {error: vi.fn(), warning: vi.fn()} as any,
      {createPlanningTag: vi.fn()} as any,
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
