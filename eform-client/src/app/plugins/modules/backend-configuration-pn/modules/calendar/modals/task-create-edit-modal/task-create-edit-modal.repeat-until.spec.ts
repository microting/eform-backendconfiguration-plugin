import {of} from 'rxjs';
import {TaskCreateEditModalComponent, TaskCreateEditModalData} from './task-create-edit-modal.component';
import {CalendarRepeatMeta} from '../../../../models/calendar';
import {useFixedUtcOffset} from '../../services/fixed-offset-date.testing';

/**
 * #1293 — the custom repeat's "til og med" date goes on the wire as a
 * date-only "yyyy-MM-dd" (the #966 pattern), never as toISOString(): a UTC+1
 * browser serialised local-midnight 10 Dec as 2026-12-09T23:00Z, the backend
 * dropped the 10 Dec occurrence and the label read back "9. december".
 *
 * Constructed directly with plain stubs, exactly like the #1295 spec next door.
 */

const DANISH = 1;
const WORKERS_P5 = [{id: 11, name: 'Anders Jensen', description: '', languageId: DANISH}];
const TEAMS_P5: {id: number; name: string; description: string; memberSiteIds: number[]}[] = [];
const WORKERS_P6 = WORKERS_P5;
const TEAMS_P6 = TEAMS_P5;
const LANGUAGES = [{id: DANISH, languageCode: 'da', name: 'Dansk', isActive: true}];

function futureDate(days = 7): string {
  const d = new Date();
  d.setDate(d.getDate() + days);
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
}

describe('TaskCreateEditModalComponent — repeat-until payload (#1293)', () => {
  let calendarService: any;
  let propertiesService: any;
  let workerTagsService: any;
  let restoreZone: (() => void) | null = null;

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

  afterEach(() => {
    restoreZone?.();
    restoreZone = null;
  });

  function saveWithCustomMeta(meta: CalendarRepeatMeta) {
    const component = build();
    component.titleControl.setValue('Weekly Thursday');
    component.assigneeControl.setValue(['s:11']);
    (component as any).customRepeatMeta = meta;
    component.repeatControl.setValue('customCurrent');
    component.onSave();
    expect(calendarService.createTask).toHaveBeenCalledTimes(1);
    return calendarService.createTask.mock.calls[0][0];
  }

  // [UTC offset, month index, day, expected wire value]
  it.each([
    [1, 11, 10, '2026-12-10'],   // Copenhagen winter — was 2026-12-09T23:00:00.000Z
    [2, 5, 11, '2026-06-11'],    // Copenhagen summer (DST) — was 2026-06-10T22:00:00.000Z
    [-5, 11, 10, '2026-12-10'],  // New York
    [1, 11, 31, '2026-12-31'],   // 31 Dec → 1 Jan boundary — was 2026-12-30T23:00:00.000Z
    [1, 0, 1, '2027-01-01'],     // was 2026-12-31T23:00:00.000Z (the PREVIOUS year)
  ])('UTC%s: until %s/%s is sent as "%s"', (offset, month, day, wire) => {
    restoreZone = useFixedUtcOffset(offset);
    const year = month === 0 ? 2027 : 2026;
    const payload = saveWithCustomMeta({
      kind: 'weeklyOne', n: 1, weekday: 4, endMode: 'until',
      untilTs: new Date(year, month, day).getTime(),
    });
    expect(payload.repeatEndMode).toBe(2);
    expect(payload.repeatUntilDate).toBe(wire);
  });

  it('after N sends the count and no until date', () => {
    const payload = saveWithCustomMeta({kind: 'weeklyOne', n: 1, weekday: 4, endMode: 'after', afterCount: 10});
    expect(payload.repeatEndMode).toBe(1);
    expect(payload.repeatOccurrences).toBe(10);
    expect(payload.repeatUntilDate).toBeNull();
  });
});
