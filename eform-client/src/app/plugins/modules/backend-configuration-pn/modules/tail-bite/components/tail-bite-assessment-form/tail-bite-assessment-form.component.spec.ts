import {SimpleChange} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {Subject, of, throwError} from 'rxjs';
import {TailBiteFactor, TailBiteOutbreakDetail} from '../../../../models';
import {BackendConfigurationPnTailBiteService} from '../../../../services';
import {TailBiteAssessmentFormComponent} from './tail-bite-assessment-form.component';

describe('TailBiteAssessmentFormComponent', () => {
  const allNo = {water: false, feed: false, activityMaterial: false, climate: false, health: false, management: false};
  const detail = (over: Partial<TailBiteOutbreakDetail> = {}): TailBiteOutbreakDetail => ({
    summary: {id: 5, locationId: 2, openedAt: '2026-10-01T06:12:00', assessed: false, openActions: 0, bittenPigs: 0, severePigs: 0, closed: false},
    ruleId: 9, ruleVersion: 1, registrationIds: [21], answers: null, actions: [], ...over,
  });
  let saveAssessment: jest.Mock;
  let component: TailBiteAssessmentFormComponent;

  const create = (d: TailBiteOutbreakDetail) => {
    component = TestBed.runInInjectionContext(() => new TailBiteAssessmentFormComponent());
    component.detail = d;
    component.ngOnChanges({detail: new SimpleChange(null, d, true)});
  };

  beforeEach(() => {
    saveAssessment = jest.fn().mockReturnValue(of({success: true}));
    TestBed.configureTestingModule({
      providers: [{provide: BackendConfigurationPnTailBiteService, useValue: {saveAssessment}}],
    });
  });

  const fillFeedYes = () => {
    component.factors.forEach((f) => component.setAnswer(f.factor, f.factor === TailBiteFactor.Feed));
    Object.assign(component.draft(TailBiteFactor.Feed).newActions[0],
      {description: 'Tjek foderautomat', responsibleSiteId: 7, followUpDate: new Date(2026, 9, 6)});
  };

  it('does not save until every factor is answered', () => {
    create(detail());
    component.save();
    expect(saveAssessment).not.toHaveBeenCalled();
    expect(component.errors.size).toBe(6);
  });

  it('opens an empty action row on a yes and refuses to save it empty', () => {
    create(detail());
    component.factors.forEach((f) => component.setAnswer(f.factor, f.factor === TailBiteFactor.Feed));
    expect(component.draft(TailBiteFactor.Feed).newActions.length).toBe(1);
    component.save();
    expect(component.errors.get(TailBiteFactor.Feed)).toBe('needsAction');
    expect(saveAssessment).not.toHaveBeenCalled();
  });

  it('saves a complete assessment and tells the page', () => {
    create(detail());
    const saved = jest.fn();
    component.saved.subscribe(saved);
    fillFeedYes();
    component.save();
    expect(saveAssessment).toHaveBeenCalledWith(5, {
      answers: {...allNo, feed: true},
      newActions: [{factor: TailBiteFactor.Feed, description: 'Tjek foderautomat', responsibleSiteId: 7, followUpDate: '2026-10-06'}],
    });
    expect(saved).toHaveBeenCalled();
  });

  it('warns that a yes turned into no withdraws open follow-ups', () => {
    create(detail({answers: {...allNo, climate: true}, actions: [
      {id: 1, factor: TailBiteFactor.Climate, description: 'Tjek ventil', responsibleSiteId: 7, followUpDate: '2026-10-04T00:00:00',
        doneAt: null, withdrawnAt: null},
    ]}));
    expect(component.withdrawsOnNo(TailBiteFactor.Climate)).toBe(false);
    component.setAnswer(TailBiteFactor.Climate, false);
    expect(component.withdrawsOnNo(TailBiteFactor.Climate)).toBe(true);
  });

  it('is read-only for a closed outbreak', () => {
    create(detail({summary: {id: 5, locationId: 2, openedAt: '2026-10-01T06:12:00', assessed: true, openActions: 0, bittenPigs: 0, severePigs: 0, closed: true},
      answers: allNo}));
    component.save();
    expect(component.readOnly).toBe(true);
    expect(saveAssessment).not.toHaveBeenCalled();
  });

  it('does not allow follow-up dates before the day the outbreak opened', () => {
    create(detail());
    const min = component.minFollowUp!;
    expect([min.getHours(), min.getMinutes()]).toEqual([0, 0]);
  });

  describe('the minimum follow-up day', () => {
    // The server compares with OpenedAt.Date (UTC). Jest cannot switch the process time zone mid-run, so a browser zone
    // is emulated through the local calendar getters, the ones that read the wrong day: the test fails on code that
    // takes the day from local getters whatever zone the runner is in, UTC included.
    let spies: jest.SpyInstance[] = [];
    const emulateZone = (offsetHours: number) => {
      const shifted = (d: Date) => new Date(d.getTime() + offsetHours * 3600000);
      spies = [
        jest.spyOn(Date.prototype, 'getFullYear').mockImplementation(function (this: Date) { return shifted(this).getUTCFullYear(); }),
        jest.spyOn(Date.prototype, 'getMonth').mockImplementation(function (this: Date) { return shifted(this).getUTCMonth(); }),
        jest.spyOn(Date.prototype, 'getDate').mockImplementation(function (this: Date) { return shifted(this).getUTCDate(); }),
      ];
    };
    afterEach(() => spies.forEach((s) => s.mockRestore()));
    const openedAt = (value: string | Date) =>
      detail({summary: {id: 5, locationId: 2, openedAt: value, assessed: false, openActions: 0, bittenPigs: 0, severePigs: 0, closed: false}});

    it('is the UTC day east of UTC, where the local day is already the next one', () => {
      emulateZone(9);
      create(openedAt('2026-09-30T23:30:00Z'));
      expect(component.minFollowUp).toEqual(new Date(2026, 8, 30));
    });

    it('is the UTC day west of UTC, also for a Date the host DateInterceptor already parsed', () => {
      emulateZone(-4);
      create(openedAt(new Date(Date.UTC(2026, 9, 1, 0, 30))));
      expect(component.minFollowUp).toEqual(new Date(2026, 9, 1));
      create(openedAt('2026-10-01T00:30:00Z'));
      expect(component.minFollowUp).toEqual(new Date(2026, 9, 1));
    });
  });

  it('keeps everything entered and says so when the server refuses the save', () => {
    saveAssessment.mockReturnValue(of({success: false, message: 'nope'}));
    create(detail());
    const saved = jest.fn();
    component.saved.subscribe(saved);
    fillFeedYes();
    component.save();
    expect(saved).not.toHaveBeenCalled();
    expect(component.saveFailed).toBe(true);
    expect(component.busy).toBe(false);
    expect(component.draft(TailBiteFactor.Feed).newActions[0].description).toBe('Tjek foderautomat');
    expect(component.draft(TailBiteFactor.Water).answer).toBe(false);
  });

  it('keeps everything entered and says so when the call fails', () => {
    saveAssessment.mockReturnValue(throwError(() => new Error('boom')));
    create(detail());
    fillFeedYes();
    component.save();
    expect(component.saveFailed).toBe(true);
    expect(component.busy).toBe(false);
    expect(component.draft(TailBiteFactor.Feed).newActions.length).toBe(1);
  });

  it('is single-flight: a second save while one is running is ignored', () => {
    const pending = new Subject<{success: boolean}>();
    saveAssessment.mockReturnValue(pending);
    create(detail());
    fillFeedYes();
    component.save();
    component.save();
    expect(saveAssessment).toHaveBeenCalledTimes(1);
    expect(component.busy).toBe(true);
    pending.next({success: true});
    pending.complete();
    expect(component.busy).toBe(false);
  });

  it('clears the failure notice when the user edits again', () => {
    saveAssessment.mockReturnValue(of({success: false}));
    create(detail());
    fillFeedYes();
    component.save();
    component.setAnswer(TailBiteFactor.Feed, true);
    expect(component.saveFailed).toBe(false);
  });

  it('derives the save hint from the same errors that block saving', () => {
    create(detail());
    expect(component.hasErrors).toBe(true);
    fillFeedYes();
    component.refresh();
    expect(component.hasErrors).toBe(false);
  });

  const resupply = (d: TailBiteOutbreakDetail) => {
    const previous = component.detail;
    component.detail = d;
    component.ngOnChanges({detail: new SimpleChange(previous, d, false)});
  };

  it('keeps a typed draft when the same outbreak is supplied again', () => {
    create(detail());
    fillFeedYes();
    resupply(detail({actions: [
      {id: 3, factor: TailBiteFactor.Water, description: 'x', responsibleSiteId: 7, followUpDate: '2026-10-04T00:00:00',
        doneAt: null, withdrawnAt: null},
    ]}));
    expect(component.draft(TailBiteFactor.Feed).answer).toBe(true);
    expect(component.draft(TailBiteFactor.Feed).newActions[0].description).toBe('Tjek foderautomat');
    expect(component.existing(TailBiteFactor.Water).length).toBe(1);
  });

  it('resets the drafts for another outbreak', () => {
    create(detail());
    fillFeedYes();
    resupply(detail({summary: {id: 6, locationId: 2, openedAt: '2026-10-01T06:12:00', assessed: false, openActions: 0, bittenPigs: 0,
      severePigs: 0, closed: false}}));
    expect(component.drafts.every((d) => d.answer === null && d.newActions.length === 0)).toBe(true);
  });

  it('resets from the next detail after a successful save', () => {
    create(detail());
    fillFeedYes();
    component.save();
    resupply(detail({answers: {...allNo, climate: true}}));
    expect(component.draft(TailBiteFactor.Climate).answer).toBe(true);
    expect(component.draft(TailBiteFactor.Feed).answer).toBe(false);
  });

  it('does not post the same actions twice when Save is clicked again before the new detail arrives', () => {
    create(detail());
    fillFeedYes();
    component.save();
    component.save();
    expect(saveAssessment).toHaveBeenCalledTimes(1);
    expect(component.draft(TailBiteFactor.Feed).newActions.length).toBe(0);
  });
});
