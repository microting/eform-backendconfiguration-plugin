import {describe, it, expect, beforeEach, vi} from 'vitest';
import type {Mock} from 'vitest';
import {CommonModule} from '@angular/common';
import {NO_ERRORS_SCHEMA} from '@angular/core';
import {ComponentFixture, TestBed} from '@angular/core/testing';
import {MatDialog} from '@angular/material/dialog';
import {TranslateModule} from '@ngx-translate/core';
import {EMPTY, Subject, of, throwError} from 'rxjs';
import {
  TailBiteComplianceReport,
  TailBiteFactor,
  TailBiteReportOutbreak,
} from '../../../../models';
import {
  BackendConfigurationPnComplianceReportService,
  BackendConfigurationPnTailBiteService,
} from '../../../../services';
import {detectChanges} from '../../../../testing/detect-changes';
import {TailBitePhotoThumbComponent} from '../../../tail-bite/components/tail-bite-photo-thumb/tail-bite-photo-thumb.component';
import {ComplianceReportStateService} from '../../store';
import {
  ComplianceTailBiteSectionComponent,
  buildComplianceTailBiteView,
} from './compliance-tail-bite-section.component';

function outbreak(id: number, patch: Partial<TailBiteReportOutbreak> = {}): TailBiteReportOutbreak {
  return {
    id,
    propertyId: 3,
    propertyName: 'Eksempelgården I/S',
    locationPath: 'Stald 3 › Sektion 2',
    ruleVersion: 1,
    openedAt: '2026-11-02T07:00:00Z',
    assessedAt: '2026-11-03T09:00:00Z',
    closedAt: null,
    bittenPigs: 6,
    severePigs: 2,
    answers: {water: false, feed: true, activityMaterial: true, climate: false, health: false, management: false},
    actions: [
      {factor: TailBiteFactor.Feed, description: 'Kontrollér foderautomaten', responsibleName: 'Jonas Skov',
        followUpDate: '2026-11-06T00:00:00', doneAt: '2026-11-06T10:00:00Z', doneByName: 'Jonas Skov'},
      {factor: TailBiteFactor.ActivityMaterial, description: 'Ekstra reb og halm', responsibleName: 'Mette Holm',
        followUpDate: '2026-11-22T00:00:00', doneAt: null, doneByName: null},
    ],
    registrations: [
      {id: 41, effectiveAt: '2026-11-02T06:30:00Z', pens: 'Sti 1, Sti 2', minor: 4, severe: 1, actionTypes: '',
        registeredByName: 'Mette Holm', photos: [{photoUuid: 'p-1', registrationId: 41}, {photoUuid: 'p-2', registrationId: 41}]},
      {id: 42, effectiveAt: '2026-11-03T06:30:00Z', pens: 'Sti 3', minor: 0, severe: 1, actionTypes: 'Halm',
        registeredByName: 'Jane Doe', photos: [{photoUuid: 'p-3', registrationId: 42}]},
    ],
    ...patch,
  };
}

const report = (outbreaks: TailBiteReportOutbreak[]): TailBiteComplianceReport => ({
  outbreaks,
  totals: {outbreaks: outbreaks.length, closedInPeriod: 1, bittenPigs: 17, severePigs: 5, actionsDone: 4, actionsTotal: 6},
});

describe('buildComplianceTailBiteView', () => {
  it('is null for a null model: the caller sees no Halebid section', () => {
    expect(buildComplianceTailBiteView(null)).toBeNull();
  });

  it('lists the yes factors in display order, and every factor for the detail', () => {
    const [row] = buildComplianceTailBiteView(report([outbreak(1)]))!.rows;

    expect(row.assessed).toBe(true);
    expect(row.yesFactors).toEqual(['Feed and feeding', 'Rooting and activity material']);
    expect(row.factors.map((f) => [f.label, f.yes])).toEqual([
      ['Water supply', false],
      ['Feed and feeding', true],
      ['Rooting and activity material', true],
      ['Barn climate', false],
      ['Herd health', false],
      ['Farm management', false],
    ]);
  });

  it('has no factors for an outbreak that is not risk assessed', () => {
    const [row] = buildComplianceTailBiteView(report([outbreak(1, {answers: null, assessedAt: null})]))!.rows;

    expect(row.assessed).toBe(false);
    expect(row.factors).toEqual([]);
    expect(row.yesFactors).toEqual([]);
    expect(row.assessedAt).toBeNull();
  });

  it('counts done actions and labels each with its factor', () => {
    const [row] = buildComplianceTailBiteView(report([outbreak(1)]))!.rows;

    expect(row.actionsDone).toBe(1);
    expect(row.actions.map((a) => a.factorLabel)).toEqual(['Feed and feeding', 'Rooting and activity material']);
    expect(row.actions[1].followUpDate.getDate()).toBe(22);
  });

  it('builds one gallery item per photo from its registration: time, pens, registrant', () => {
    const [row] = buildComplianceTailBiteView(report([outbreak(1)]))!.rows;

    expect(row.photos.map((p) => [p.uuid, p.registrationId, p.pens, p.siteName])).toEqual([
      ['p-1', 41, 'Sti 1, Sti 2', 'Mette Holm'],
      ['p-2', 41, 'Sti 1, Sti 2', 'Mette Holm'],
      ['p-3', 42, 'Sti 3', 'Jane Doe'],
    ]);
    expect(row.photos[0].effectiveAt).toEqual(new Date('2026-11-02T06:30:00Z'));
  });

  it('names the property only when the section spans more than one', () => {
    expect(buildComplianceTailBiteView(report([outbreak(1), outbreak(2)]))!.showProperty).toBe(false);
    expect(buildComplianceTailBiteView(report([outbreak(1), outbreak(2, {propertyId: 4})]))!.showProperty).toBe(true);
  });
});

describe('ComplianceTailBiteSectionComponent', () => {
  let fixture: ComponentFixture<ComplianceTailBiteSectionComponent>;
  let component: ComplianceTailBiteSectionComponent;
  let trigger: Subject<void>;
  let tailBite: Mock;
  let dialogOpen: Mock;
  let state: ComplianceReportStateService;

  const root = (): HTMLElement => fixture.nativeElement;
  const section = () => root().querySelector('#complianceTailBiteSection');

  /** Answers the next fetch with `response`, fires the trigger and renders. */
  const fetchWith = (response: unknown) => {
    tailBite.mockReturnValue(response);
    trigger.next();
    detectChanges(fixture);
  };

  beforeEach(async () => {
    trigger = new Subject<void>();
    tailBite = vi.fn();
    dialogOpen = vi.fn();
    await TestBed.configureTestingModule({
      imports: [ComplianceTailBiteSectionComponent, TranslateModule.forRoot()],
      providers: [
        ComplianceReportStateService,
        {provide: BackendConfigurationPnComplianceReportService, useValue: {tailBite}},
        {provide: BackendConfigurationPnTailBiteService, useValue: {getPhotoBlob: vi.fn().mockReturnValue(EMPTY)}},
        {provide: MatDialog, useValue: {open: dialogOpen}},
      ],
      schemas: [NO_ERRORS_SCHEMA],
    })
      .overrideComponent(ComplianceTailBiteSectionComponent, {
        set: {imports: [CommonModule, TranslateModule, TailBitePhotoThumbComponent], schemas: [NO_ERRORS_SCHEMA]},
      })
      .compileComponents();

    fixture = TestBed.createComponent(ComplianceTailBiteSectionComponent);
    component = fixture.componentInstance;
    component.trigger = trigger;
    state = TestBed.inject(ComplianceReportStateService);
    detectChanges(fixture);
  });

  it('fetches only on the trigger, with the state\'s current request model', () => {
    expect(tailBite).not.toHaveBeenCalled();

    fetchWith(of({success: true, model: null}));

    expect(tailBite).toHaveBeenCalledTimes(1);
    expect(tailBite).toHaveBeenCalledWith(state.requestModel);
  });

  it('renders nothing at all for a null model', () => {
    fetchWith(of({success: true, model: null}));

    expect(section()).toBeNull();
    expect(root().textContent!.trim()).toBe('');
  });

  it.each([
    ['a failed result', () => of({success: false, message: 'Request body is required.'})],
    ['a transport error', () => throwError(() => new Error('down'))],
    ['a call the interceptor completed without a value', () => EMPTY],
  ])('renders nothing after %s, and hides a section shown before', (_name, response) => {
    fetchWith(of({success: true, model: report([outbreak(1)])}));
    expect(section()).not.toBeNull();

    fetchWith(response());

    expect(section()).toBeNull();
  });

  it('keeps answering triggers after a failed fetch', () => {
    fetchWith(throwError(() => new Error('down')));
    fetchWith(of({success: true, model: report([])}));

    expect(section()).not.toBeNull();
  });

  it('says there were no outbreaks, with the totals and no table, for an empty list', () => {
    fetchWith(of({success: true, model: report([])}));

    expect(section()).not.toBeNull();
    expect(root().querySelector('#complianceTailBiteEmpty')!.textContent!.trim()).toBe('No outbreaks in the period');
    expect(root().querySelector('#complianceTailBiteTable')).toBeNull();
    expect(root().querySelector('#complianceTailBiteTotalOutbreaks strong')!.textContent!.trim()).toBe('0');
  });

  it('renders one row per outbreak and the four totals', () => {
    fetchWith(of({success: true, model: report([outbreak(7), outbreak(8, {answers: null, assessedAt: null, actions: []})])}));

    expect(root().querySelector('#complianceTailBiteEmpty')).toBeNull();
    expect(Array.from(root().querySelectorAll('tbody tr')).map((tr) => tr.id)).toEqual([
      'complianceTailBiteRow-7',
      'complianceTailBiteRow-8',
    ]);
    const total = (id: string) => root().querySelector(`#${id} strong`)!.textContent!.replace(/\s+/g, ' ').trim();
    expect(total('complianceTailBiteTotalOutbreaks')).toBe('2');
    expect(total('complianceTailBiteTotalClosed')).toBe('1');
    expect(total('complianceTailBiteTotalPigs')).toBe('17');
    expect(total('complianceTailBiteTotalActions')).toBe('4 of 6');

    const row7 = root().querySelector('#complianceTailBiteRow-7')!;
    expect(row7.textContent).toContain('Stald 3 › Sektion 2');
    expect(root().querySelector('#complianceTailBiteYes-7')!.textContent!.trim()).toBe(
      'Feed and feeding, Rooting and activity material',
    );
    expect(root().querySelector('#complianceTailBiteYes-8')!.textContent!.trim()).toBe('Not assessed');
  });

  it('says None for an assessed outbreak with no yes factor', () => {
    const allNo = {water: false, feed: false, activityMaterial: false, climate: false, health: false, management: false};
    fetchWith(of({success: true, model: report([outbreak(7, {answers: allNo})])}));

    expect(root().querySelector('#complianceTailBiteYes-7')!.textContent!.trim()).toBe('None');
  });

  it('toggles the detail row with its factors, actions and photo thumbnails', () => {
    fetchWith(of({success: true, model: report([outbreak(7)])}));
    const toggle = root().querySelector('#complianceTailBiteToggle-7') as HTMLButtonElement;
    expect(root().querySelector('#complianceTailBiteDetail-7')).toBeNull();
    expect(toggle.getAttribute('aria-expanded')).toBe('false');

    toggle.click();
    detectChanges(fixture);

    const detail = root().querySelector('#complianceTailBiteDetail-7')!;
    expect(detail).not.toBeNull();
    expect(toggle.getAttribute('aria-expanded')).toBe('true');
    expect(detail.querySelectorAll('.badge').length).toBe(6);
    expect(detail.querySelectorAll('.badge-warning').length).toBe(2);
    expect(detail.querySelectorAll('li').length).toBe(2);
    expect(detail.querySelectorAll('app-tail-bite-photo-thumb').length).toBe(3);

    toggle.click();
    detectChanges(fixture);

    expect(root().querySelector('#complianceTailBiteDetail-7')).toBeNull();
  });

  it('keeps an open detail row open over a re-fetch', () => {
    fetchWith(of({success: true, model: report([outbreak(7)])}));
    (root().querySelector('#complianceTailBiteToggle-7') as HTMLButtonElement).click();

    fetchWith(of({success: true, model: report([outbreak(7)])}));

    expect(root().querySelector('#complianceTailBiteDetail-7')).not.toBeNull();
  });

  it('opens the viewer on the clicked photo, walking that outbreak\'s photos', () => {
    fetchWith(of({success: true, model: report([outbreak(7)])}));
    (root().querySelector('#complianceTailBiteToggle-7') as HTMLButtonElement).click();
    detectChanges(fixture);

    (root().querySelector('#complianceTailBitePhoto-7-2') as HTMLButtonElement).click();

    expect(dialogOpen).toHaveBeenCalledTimes(1);
    const data = dialogOpen.mock.calls[0][1].data;
    expect(data.index).toBe(2);
    expect(data.items.map((i: {uuid: string}) => i.uuid)).toEqual(['p-1', 'p-2', 'p-3']);
    expect(data.blobs).toBe(component.photoBlobs);
  });
});
