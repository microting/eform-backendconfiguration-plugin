import {NO_ERRORS_SCHEMA} from '@angular/core';
import {ComponentFixture, TestBed} from '@angular/core/testing';
import {TranslateModule} from '@ngx-translate/core';
import {of} from 'rxjs';
import {ItemsPlanningPnTagsService} from 'src/app/plugins/modules/items-planning-pn/services';
import {
  BackendConfigurationPnCalendarService,
  BackendConfigurationPnPropertiesService,
} from '../../../../services';
import {ComplianceReportStateService} from '../../store';
import {ComplianceReportFiltersComponent} from './compliance-report-filters.component';

/**
 * #1299 on the filter bar: the status filter ("Ikke udførte opgaver") is not
 * rendered in Oversigt — it used to be rendered DISABLED — and stays in
 * Detaljer and Rapport; the period select offers "År til dato + 1 år".
 *
 * The mtx-selects are unknown elements under NO_ERRORS_SCHEMA, so presence is
 * asserted on the `id`s the e2e specs use. The state service is the real one.
 */
describe('ComplianceReportFiltersComponent (#1299)', () => {
  let fixture: ComponentFixture<ComplianceReportFiltersComponent>;
  let component: ComplianceReportFiltersComponent;
  let state: ComplianceReportStateService;

  const statusSelect = (): HTMLElement | null =>
    fixture.nativeElement.querySelector('#complianceFilterStatus');

  beforeEach(() => {
    // setFilter's debounce is a real setTimeout; nothing here needs it to run.
    jest.useFakeTimers();
    state = new ComplianceReportStateService();

    TestBed.configureTestingModule({
      declarations: [ComplianceReportFiltersComponent],
      imports: [TranslateModule.forRoot()],
      providers: [
        {provide: ComplianceReportStateService, useValue: state},
        {
          provide: BackendConfigurationPnPropertiesService,
          useValue: {
            getAllPropertiesDictionary: jest.fn().mockReturnValue(of({success: true, model: []})),
            getDeviceUsersFiltered: jest.fn().mockReturnValue(of({success: true, model: []})),
          },
        },
        {
          provide: BackendConfigurationPnCalendarService,
          useValue: {getBoards: jest.fn().mockReturnValue(of({success: true, model: []}))},
        },
        {
          provide: ItemsPlanningPnTagsService,
          useValue: {getPlanningsTags: jest.fn().mockReturnValue(of({success: true, model: []}))},
        },
      ],
      schemas: [NO_ERRORS_SCHEMA],
    });

    fixture = TestBed.createComponent(ComplianceReportFiltersComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  afterEach(() => {
    fixture.destroy();
    jest.useRealTimers();
  });

  it('does not render the status filter in Oversigt', () => {
    expect(state.mode).toBe('overview');

    expect(statusSelect()).toBeNull();
    expect(component.statusHidden).toBe(true);
  });

  it.each(['details', 'report'] as const)('renders the status filter in %s', (mode) => {
    state.setMode(mode);
    fixture.detectChanges();

    expect(statusSelect()).not.toBeNull();
    expect(component.statusHidden).toBe(false);
  });

  it('hides it again on the way back to Oversigt, keeping its value for the drill-down', () => {
    state.setMode('details');
    fixture.detectChanges();
    component.onStatusChange('done');
    expect(statusSelect()).not.toBeNull();

    state.resetToOverview();
    fixture.detectChanges();

    expect(statusSelect()).toBeNull();
    expect(state.filters.status).toBe('done');
  });

  it('keeps the other filters visible in Oversigt', () => {
    for (const id of [
      'complianceFilterProperty',
      'complianceFilterBoard',
      'complianceTagFilter',
      'complianceFilterEmployee',
      'complianceFilterPeriod',
    ]) {
      expect(fixture.nativeElement.querySelector(`#${id}`)).not.toBeNull();
    }
  });

  it('offers "Year to date + 1 year" right after "Year to date"', () => {
    const values = component.periodOptions.map((o) => o.value);
    expect(values).toEqual(['1', '3', '6', '12', 'ytd', 'ytd1y', 'custom']);
    expect(component.periodOptions.find((o) => o.value === 'ytd1y').label).toBe('Year to date + 1 year');
  });

  it('choosing it goes through setFilter like every other preset', () => {
    const setFilter = jest.spyOn(state, 'setFilter');

    component.onPeriodChange('ytd1y');

    expect(setFilter).toHaveBeenCalledWith({periodPreset: 'ytd1y'});
    expect(component.periodPreset).toBe('ytd1y');
  });
});
