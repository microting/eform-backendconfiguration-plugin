import {NO_ERRORS_SCHEMA} from '@angular/core';
import {ComponentFixture, TestBed} from '@angular/core/testing';
import {MAT_DIALOG_DATA, MatDialogRef} from '@angular/material/dialog';
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
      declarations: [CalendarCompleteEventModalComponent],
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
});
