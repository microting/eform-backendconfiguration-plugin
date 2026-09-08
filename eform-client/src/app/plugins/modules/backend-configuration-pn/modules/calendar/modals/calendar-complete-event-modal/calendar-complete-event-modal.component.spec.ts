import {Component, Input, NO_ERRORS_SCHEMA} from '@angular/core';
import {ComponentFixture, TestBed} from '@angular/core/testing';
import {MAT_DIALOG_DATA, MatDialogRef} from '@angular/material/dialog';
import {By} from '@angular/platform-browser';
import {TranslateModule} from '@ngx-translate/core';
import {of} from 'rxjs';
import {EFormService} from 'src/app/common/services';
import {
  CalendarCompleteEventModalComponent,
  CalendarCompleteEventModalData,
} from './calendar-complete-event-modal.component';
import {
  BackendConfigurationPnCompliancesService,
  BackendConfigurationPnCalendarService,
  BackendConfigurationPnPropertiesService,
} from '../../../../services';

/**
 * Covers the grouped worker dropdown added by the modal redesign, plus the save
 * gating and preselect behaviour the redesign touches only incidentally — those
 * are pinned here so a layout change cannot quietly alter them.
 */
const WORKERS = [
  {id: 1, name: 'Anders Jensen', description: ''},
  {id: 2, name: 'Anton Hansen', description: ''},
  {id: 3, name: 'René Schultz Madsen', description: ''},
];

/**
 * A real stub for `app-case-edit-element`, because `NO_ERRORS_SCHEMA` alone
 * cannot observe an input: it makes Angular ACCEPT any unknown property
 * binding silently, which is exactly the failure mode #1159 has to be guarded
 * against — delete `[caseId]` from the template and nothing complains, in this
 * suite or in the browser, until a picture upload lands on caseId 0.
 *
 * Declaring the selector makes Angular MATCH this directive instead, so the
 * bound value becomes readable. It renders nothing; the assertion is on the
 * component instance, never on the DOM.
 */
@Component({selector: 'app-case-edit-element', template: '', standalone: false})
class StubCaseEditElement {
  @Input() element: any;
  @Input() showSectionTitle: any;
  @Input() caseId: any;
}

describe('CalendarCompleteEventModalComponent', () => {
  let fixture: ComponentFixture<CalendarCompleteEventModalComponent>;
  let component: CalendarCompleteEventModalComponent;

  const dialogRef = {close: jest.fn()};
  const propertiesService = {getLinkedSites: jest.fn()};
  const calendarService = {prepareComplete: jest.fn()};
  const compliancesService = {getCase: jest.fn(), updateCaseFromCalendar: jest.fn()};
  const eFormService = {getSingle: jest.fn()};

  async function setup(data: Partial<CalendarCompleteEventModalData> = {}) {
    jest.clearAllMocks();
    propertiesService.getLinkedSites.mockReturnValue(of({success: true, model: WORKERS}));
    // Stop the chain right after the worker list loads: this suite is about
    // grouping and gating, not the case-loading pipeline.
    calendarService.prepareComplete.mockReturnValue(of({success: false, model: null}));

    await TestBed.configureTestingModule({
      declarations: [CalendarCompleteEventModalComponent, StubCaseEditElement],
      imports: [TranslateModule.forRoot()],
      providers: [
        {provide: MatDialogRef, useValue: dialogRef},
        {provide: BackendConfigurationPnPropertiesService, useValue: propertiesService},
        {provide: BackendConfigurationPnCalendarService, useValue: calendarService},
        {provide: BackendConfigurationPnCompliancesService, useValue: compliancesService},
        {provide: EFormService, useValue: eFormService},
        {
          provide: MAT_DIALOG_DATA,
          useValue: {
            taskId: 1,
            complianceId: null,
            occurrenceDate: '2026-08-19',
            propertyId: 5,
            assigneeIds: [],
            ...data,
          } as CalendarCompleteEventModalData,
        },
      ],
      schemas: [NO_ERRORS_SCHEMA],
    }).compileComponents();

    fixture = TestBed.createComponent(CalendarCompleteEventModalComponent);
    component = fixture.componentInstance;
    component.ngOnInit();
  }

  afterEach(() => TestBed.resetTestingModule());

  // U14
  it('splits workers into assigned and other groups', async () => {
    await setup({assigneeIds: [3]});

    const groups = component.groupedSites.map(s => s.group);
    // Stable keys, translated in the template — see buildGroupedSites.
    expect(groups).toEqual(['assigned', 'other', 'other']);
    expect(component.groupedSites[0].name).toBe('René Schultz Madsen');
  });

  it('keeps every worker in the list when grouping', async () => {
    await setup({assigneeIds: [3]});
    expect(component.groupedSites.map(s => s.id).sort()).toEqual([1, 2, 3]);
  });

  // U15 — an empty "Assigned workers" header would be worse than no grouping.
  it('leaves the list ungrouped when the event has no assignees', async () => {
    await setup({assigneeIds: []});
    expect(component.groupedSites.every(s => s.group === undefined)).toBe(true);
    expect(component.groupedSites.length).toBe(3);
  });

  // U16
  it('leaves the list ungrouped when every worker is assigned', async () => {
    await setup({assigneeIds: [1, 2, 3]});
    expect(component.groupedSites.every(s => s.group === undefined)).toBe(true);
  });

  // U17
  it('does not allow saving until a worker and a done date are both set', async () => {
    await setup({assigneeIds: [3]});
    component.loading = false;

    component.selectedWorkerId = null;
    component.replyElement.doneAt = null;
    expect(component.canSave).toBe(false);

    component.selectedWorkerId = 3;
    expect(component.canSave).toBe(false);

    component.replyElement.doneAt = new Date();
    expect(component.canSave).toBe(true);

    component.isSaving = true;
    expect(component.canSave).toBe(false);
  });

  // U18
  it('preselects the only assigned worker', async () => {
    await setup({assigneeIds: [2]});
    expect(component.selectedWorkerId).toBe(2);
  });

  it('declines to guess a worker for a multi-assignee event', async () => {
    await setup({assigneeIds: [1, 2]});
    expect(component.selectedWorkerId).toBeNull();
  });

  it('treats a single-section eForm as needing no nav and no section headings', async () => {
    await setup();
    component.replyElement.elementList = [{id: 1, label: 'Kvittering'}] as any;
    expect(component.hasMultipleSections).toBe(false);
    expect(component.showSectionTitles).toBe(false);
  });

  it('shows the nav and section headings once there is more than one section', async () => {
    await setup();
    component.replyElement.elementList = [
      {id: 1, label: 'Kvittering'},
      {id: 2, label: 'Sikkerhed'},
    ] as any;
    expect(component.hasMultipleSections).toBe(true);
    expect(component.showSectionTitles).toBe(true);
  });

  /**
   * #1159. `element-picture` sniffs the case id out of the ROUTER URL, which
   * inside a MatDialog is the calendar's URL and yields NaN → 0 → "Sagen blev
   * ikke fundet" on every upload. The fix was one template binding,
   * `[caseId]="prepared?.sdkCaseId"`, and nothing in a runner that executes
   * covered it: `NO_ERRORS_SCHEMA` accepts the binding's absence just as
   * quietly as its presence, so only the explicit stub above can see it.
   *
   * These render the template, which the rest of this suite never does — the
   * suites above assert component state only.
   */
  it('passes the SDK case id down to every eForm section', async () => {
    await setup();
    component.prepared = {sdkCaseId: 4242} as any;
    component.replyElement.elementList = [
      {id: 1, label: 'Kvittering'},
      {id: 2, label: 'Sikkerhed'},
    ] as any;

    fixture.detectChanges();

    const sections = fixture.debugElement.queryAll(By.directive(StubCaseEditElement));
    expect(sections.length).toBe(2);
    // The assertion #1159 lives or dies by. Drop `[caseId]` from the template
    // and this is `undefined`, with no other symptom anywhere.
    for (const section of sections) {
      expect(section.componentInstance.caseId).toBe(4242);
    }
  });

  it('sends no id at all, rather than 0, before prepareComplete has answered', async () => {
    // `prepared` is null until the prepare call lands. The safe navigation in
    // `prepared?.sdkCaseId` is what makes that a nullish id (Angular's `?.`
    // short-circuits to null, not undefined): a bare `prepared.sdkCaseId`
    // would throw and take the whole render down, and a `|| 0` would hand the
    // child the very id #1159 was about.
    await setup();
    component.replyElement.elementList = [{id: 1, label: 'Kvittering'}] as any;

    fixture.detectChanges();

    const [only] = fixture.debugElement.queryAll(By.directive(StubCaseEditElement));
    expect(component.prepared).toBeNull();
    expect(only.componentInstance.caseId).toBeNull();
    // The point of the assertion above: NOT the falsy id that produced
    // "Sagen blev ikke fundet".
    expect(only.componentInstance.caseId).not.toBe(0);
  });
});
