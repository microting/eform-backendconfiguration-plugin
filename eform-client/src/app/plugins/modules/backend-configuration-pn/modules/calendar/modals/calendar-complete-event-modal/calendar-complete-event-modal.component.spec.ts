import {Component, Input, NO_ERRORS_SCHEMA, QueryList} from '@angular/core';
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
  BackendConfigurationPnCasesService,
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
  const casesService = {updateCase: jest.fn()};

  async function setup(data: Partial<CalendarCompleteEventModalData> = {}, assignedSiteId: number | null = null) {
    jest.clearAllMocks();
    propertiesService.getLinkedSites.mockReturnValue(of({success: true, model: WORKERS}));
    // Stop the chain right after the worker list loads: this suite is about
    // grouping and gating, not the case-loading pipeline. With an assignedSiteId,
    // prepare succeeds (so the preselect sees it) and the template lookup stops the chain.
    calendarService.prepareComplete.mockReturnValue(assignedSiteId == null
      ? of({success: false, model: null})
      : of({success: true, model: {sdkCaseId: 10, templateId: 20, propertyId: 5, complianceId: 30, assignedSiteId}}));
    eFormService.getSingle.mockReturnValue(of({success: false, model: null}));

    await TestBed.configureTestingModule({
      declarations: [CalendarCompleteEventModalComponent, StubCaseEditElement],
      imports: [TranslateModule.forRoot()],
      providers: [
        {provide: MatDialogRef, useValue: dialogRef},
        {provide: BackendConfigurationPnPropertiesService, useValue: propertiesService},
        {provide: BackendConfigurationPnCalendarService, useValue: calendarService},
        {provide: BackendConfigurationPnCompliancesService, useValue: compliancesService},
        {provide: EFormService, useValue: eFormService},
        {provide: BackendConfigurationPnCasesService, useValue: casesService},
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
    // `.every()` is vacuously true on an empty array — the length is what stops this
    // passing against a groupedSites that never got built. Same guard as its siblings.
    expect(component.groupedSites.length).toBe(3);
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

  // -------------------------------------------------------------------
  // #1236 — worker-tag ("team") members group as assigned, but never preselect.
  // -------------------------------------------------------------------

  it('groups a team member under "assigned to this event"', async () => {
    await setup({assigneeIds: [], teamAssigneeIds: [2]});

    expect(component.groupedSites.map(s => s.group)).toEqual(['assigned', 'other', 'other']);
    expect(component.groupedSites[0].id).toBe(2);
  });

  // Not named for de-duplication: `inGroup`/`rest` are filters over `this.sites`, so a
  // site cannot appear twice however the union is built. The overlap is here to make the
  // union path realistic, not because this test could catch a duplicate.
  it('groups explicit assignees and team members together under one assigned group', async () => {
    // Site 1 is both an explicit assignee and a member of the assigned team.
    await setup({assigneeIds: [1], teamAssigneeIds: [1, 3]});

    const assigned = component.groupedSites.filter(s => s.group === 'assigned');
    expect(assigned.map(s => s.id)).toEqual([1, 3]);
    expect(component.groupedSites.filter(s => s.group === 'other').map(s => s.id)).toEqual([2]);
    // Grouping must not add or drop a worker.
    expect(component.groupedSites.length).toBe(3);
  });

  /**
   * The decision #1236 records. A lone TEAM member is grouped as assigned but is not
   * chosen for the user: the control this would set is the site recorded as having
   * COMPLETED the case.
   */
  it('does not preselect a lone team member', async () => {
    await setup({assigneeIds: [], teamAssigneeIds: [2]});
    expect(component.selectedWorkerId).toBeNull();
  });

  /**
   * #1352 — a team-only event's case is materialised on one team member. That member is
   * where the case lives, not who did the work, so it is not preselected either.
   */
  it('does not preselect the team member a team-only case was materialised on', async () => {
    await setup({assigneeIds: [], teamAssigneeIds: [2, 3]}, 2);
    expect(component.selectedWorkerId).toBeNull();
  });

  it('still preselects the deployed site of an event with no assignees at all', async () => {
    await setup({assigneeIds: [], teamAssigneeIds: []}, 2);
    expect(component.selectedWorkerId).toBe(2);
  });

  it('still preselects a lone explicit assignee when a team is also assigned', async () => {
    await setup({assigneeIds: [1], teamAssigneeIds: [2, 3]});
    expect(component.selectedWorkerId).toBe(1);
  });

  // Deliberately NOT named for the union path. buildGroupedSites bails out on
  // `inGroup.length === 0 || rest.length === 0`, and both arms produce the same
  // ungrouped list, so this test cannot tell "the team covered everyone, leaving no
  // 'other'" from "the team was ignored, leaving no 'assigned'" — it pins the fallback
  // only. That the team half reaches `assigned` at all is pinned above, by
  // 'groups a team member under "assigned to this event"'. The length assertion is what
  // stops this passing against a groupedSites that came out empty.
  it('leaves the list ungrouped when the split would leave a group empty', async () => {
    await setup({assigneeIds: [], teamAssigneeIds: [1, 2, 3]});
    expect(component.groupedSites.every(s => s.group === undefined)).toBe(true);
    expect(component.groupedSites.length).toBe(3);
  });

  // A caller with no team information at all — the calendar's Compliance view
  // synthesises its task without one — must group exactly as before the field existed.
  //
  // The selectedWorkerId half is NOT a claim that such an event never preselects: with
  // both halves empty, applyPreselect falls through to `prepared.assignedSiteId`. That
  // branch DOES run here — it just declines: `setup()` stubs prepareComplete to fail, so
  // `prepared` stays null, `prepared?.assignedSiteId` is undefined and the `!= null`
  // guard rejects it. The null is a stub artifact, not a guarantee. What this pins is
  // that the ABSENT teamAssigneeIds does not itself select anybody.
  it('groups as an unassigned event, and selects nobody off a team, when teamAssigneeIds is omitted', async () => {
    await setup({assigneeIds: []});
    expect(component.groupedSites.every(s => s.group === undefined)).toBe(true);
    expect(component.groupedSites.length).toBe(3);
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
  /**
   * #1373 — edit mode, opened by Rapport for a COMPLETED log: no prepare, the
   * existing case with its own done date and completer, saved through
   * `PUT .../cases` (`BackendConfigurationCaseService.Update`).
   */
  describe('edit mode (#1373)', () => {
    const storedDoneAt = new Date('2026-05-13T10:00:00Z');
    const edit = {sdkCaseId: 4002, checkListId: 509, completedBySiteId: 2, completedByName: 'Anton Hansen'};

    async function setupEdit(editData = edit) {
      await setup({complianceId: 2, edit: editData});
      // setup() stops the chain at the template; edit mode loads it for real.
      eFormService.getSingle.mockReturnValue(of({success: true, model: {id: 509}}));
      compliancesService.getCase.mockReturnValue(
        of({success: true, model: {id: 4002, label: 'L', doneAt: storedDoneAt, elementList: []}}));
      casesService.updateCase.mockReturnValue(of({success: true}));
      component.ngOnInit();
      // The template is never rendered here, so @ViewChildren is unset; saveCase reads it.
      // The case has no elements, so the rendered list would be empty too.
      component.editElements = new QueryList<any>();
    }

    it('skips prepare and loads the existing case on its own template', async () => {
      await setupEdit();

      expect(calendarService.prepareComplete).not.toHaveBeenCalled();
      expect(eFormService.getSingle).toHaveBeenCalledWith(509);
      expect(compliancesService.getCase).toHaveBeenCalledWith(4002, 509);
      expect(component.caseId).toBe(4002);
      expect(component.loading).toBe(false);
    });

    it('keeps the stored done date and the completer', async () => {
      await setupEdit();

      expect(component.replyElement.doneAt).toBe(storedDoneAt);
      expect(component.selectedWorkerId).toBe(2);
      expect(component.canSave).toBe(true);
    });

    it('lists a completer who is no longer linked to the property', async () => {
      await setupEdit({...edit, completedBySiteId: 99, completedByName: 'Jane Doe'});

      expect(component.selectedWorkerId).toBe(99);
      expect(component.sites.find(s => s.id === 99)?.name).toBe('Jane Doe');
    });

    it('saves through PUT cases with the case id and completer, the stored date unchanged', async () => {
      await setupEdit();
      component.saveCase();

      expect(compliancesService.updateCaseFromCalendar).not.toHaveBeenCalled();
      const [request] = casesService.updateCase.mock.calls[0];
      expect(request.id).toBe(4002);
      expect(request.extraId).toBe(2);
      expect(request.siteId).toBe(2);
      expect(request.doneAt).toBe(storedDoneAt);
      expect(dialogRef.close).toHaveBeenCalledWith({saved: true});
    });

    it('sends a PICKED day as that day\'s UTC midnight', async () => {
      await setupEdit();
      component.replyElement.doneAt = new Date(2026, 4, 11);
      component.saveCase();

      const [request] = casesService.updateCase.mock.calls[0];
      expect(request.doneAt.toISOString()).toBe('2026-05-11T00:00:00.000Z');
    });

    it('cannot save a done date after today', async () => {
      await setupEdit();
      const tomorrow = new Date();
      tomorrow.setDate(tomorrow.getDate() + 1);
      component.replyElement.doneAt = tomorrow;

      expect(component.canSave).toBe(false);
    });
  });
});
