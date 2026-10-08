import {ComponentFixture, TestBed} from '@angular/core/testing';
import {Overlay} from '@angular/cdk/overlay';
import {SimpleChange} from '@angular/core';
import {MatDialog} from '@angular/material/dialog';
import {TranslateModule, TranslateService} from '@ngx-translate/core';
import {NgModel} from '@angular/forms';
import {By} from '@angular/platform-browser';
import {MtxSelect} from '@ng-matero/extensions/select';
import {NEVER, Subject, of, throwError} from 'rxjs';
import {TailBiteFactor, TailBiteOutbreakAction} from '../../../../models';
import {BackendConfigurationPnTailBiteService} from '../../../../services';
import {TailBiteFollowUpsComponent, actionState, canChangeAction} from './tail-bite-follow-ups.component';

const action = (id: number, over: Partial<TailBiteOutbreakAction> = {}): TailBiteOutbreakAction => ({
  id, factor: TailBiteFactor.Climate, description: 'Tjek ventil', responsibleSiteId: 7, followUpDate: '2026-10-06T00:00:00',
  doneAt: null, withdrawnAt: null, ...over,
});

describe('actionState', () => {
  const today = new Date(2026, 9, 7, 23, 59);
  it('puts withdrawn before done before overdue before open', () => {
    expect(actionState(action(1, {withdrawnAt: 'x', doneAt: 'y'}), today)).toBe('withdrawn');
    expect(actionState(action(1, {doneAt: 'y'}), today)).toBe('done');
    expect(actionState(action(1), today)).toBe('overdue');
    expect(actionState(action(1, {followUpDate: '2026-10-08T00:00:00'}), today)).toBe('open');
  });

  it('does not treat a follow-up due today as overdue, at any time of day', () => {
    expect(actionState(action(1, {followUpDate: '2026-10-07T00:00:00'}), today)).toBe('open');
    expect(actionState(action(1, {followUpDate: '2026-10-07T00:00:00'}), new Date(2026, 9, 7, 0, 0))).toBe('open');
  });
});

describe('canChangeAction', () => {
  it('only an open or overdue action can be withdrawn or reassigned (the server refuses done and withdrawn ones)', () => {
    expect(canChangeAction('open')).toBe(true);
    expect(canChangeAction('overdue')).toBe(true);
    expect(canChangeAction('done')).toBe(false);
    expect(canChangeAction('withdrawn')).toBe(false);
  });
});

describe('TailBiteFollowUpsComponent', () => {
  let service: Record<string, jest.Mock>;
  let dialogOpen: jest.Mock;
  let component: TailBiteFollowUpsComponent;
  let changed: jest.Mock;

  /** The next text dialog: `save(text)` presses Save with that text, `cancel()` closes it; `close` is the ref's close. */
  const textDialog = () => {
    const saved = new Subject<string>();
    const closed = new Subject<void>();
    const close = jest.fn(() => closed.next());
    dialogOpen.mockReturnValueOnce({componentInstance: {saved}, close, afterClosed: () => closed});
    return {save: (text: string) => saved.next(text), cancel: () => closed.next(), close};
  };

  beforeEach(() => {
    service = {
      setActionDone: jest.fn().mockReturnValue(of({success: true})),
      withdrawAction: jest.fn().mockReturnValue(of({success: true})),
      reassignAction: jest.fn().mockReturnValue(of({success: true})),
    };
    dialogOpen = jest.fn();
    TestBed.configureTestingModule({
      providers: [
        {provide: BackendConfigurationPnTailBiteService, useValue: service},
        {provide: MatDialog, useValue: {open: dialogOpen}},
        {provide: Overlay, useValue: {scrollStrategies: {reposition: () => ({})}}},
        {provide: TranslateService, useValue: {instant: (k: string) => k}},
      ],
    });
    component = TestBed.runInInjectionContext(() => new TailBiteFollowUpsComponent());
    component.detail = {
      summary: {id: 5, locationId: 2, openedAt: '2026-10-01T06:12:00', assessed: true, openActions: 1, closed: false, bittenPigs: 3, severePigs: 1},
      ruleId: 9, ruleVersion: 1, registrationIds: [], answers: null,
      actions: [action(2, {followUpDate: '2026-10-08T00:00:00'}), action(1)],
    };
    component.workers = [{siteId: 7, name: 'Jane Doe', assignable: true},
      {siteId: 8, name: 'John Doe', assignable: true}];
    component.today = new Date(2026, 9, 7);
    changed = jest.fn();
    component.changed.subscribe(changed);
  });

  it('lists actions by follow-up date and derives their state', () => {
    expect(component.actions.map((a) => a.id)).toEqual([1, 2]);
    expect(component.state(action(1))).toBe('overdue');
    expect(component.state(action(2, {followUpDate: '2026-10-08T00:00:00'}))).toBe('open');
    expect(component.state(action(3, {doneAt: '2026-10-02T07:00:00'}))).toBe('done');
    expect(component.state(action(4, {withdrawnAt: '2026-10-02T07:00:00'}))).toBe('withdrawn');
  });

  it('sorts and states actions whose dates the host DateInterceptor turned into Dates (midnight UTC)', () => {
    component.detail = {...component.detail, actions: [
      action(2, {followUpDate: new Date(Date.UTC(2026, 9, 8))}),
      action(1, {followUpDate: new Date(Date.UTC(2026, 9, 6))}),
      action(3, {followUpDate: new Date(Date.UTC(2026, 9, 7))}),
    ]};
    expect(component.actions.map((a) => a.id)).toEqual([1, 3, 2]);
    expect(component.actions.map((a) => component.state(a))).toEqual(['overdue', 'open', 'open']);
    const due = component.followUpDate(component.actions[1]);
    expect([due.getFullYear(), due.getMonth(), due.getDate()]).toEqual([2026, 9, 7]);
  });

  it('marks done and undoes done, telling the parent to reload', () => {
    component.toggleDone(action(1));
    expect(service.setActionDone).toHaveBeenLastCalledWith(1, true);
    component.toggleDone(action(1, {doneAt: '2026-10-02T07:00:00'}));
    expect(service.setActionDone).toHaveBeenLastCalledWith(1, false);
    expect(changed).toHaveBeenCalledTimes(2);
    expect(component.busy).toBe(false);
  });

  it('reloads the parent when the server refuses or fails, so the UI shows server truth', () => {
    service.setActionDone.mockReturnValueOnce(of({success: false, message: 'The outbreak is closed.'}));
    component.toggleDone(action(1));
    expect(changed).toHaveBeenCalledTimes(1);
    expect(component.busy).toBe(false);
    service.setActionDone.mockReturnValueOnce(throwError(() => new Error('boom')));
    component.toggleDone(action(1));
    expect(changed).toHaveBeenCalledTimes(2);
    expect(component.busy).toBe(false);
  });

  it('is single-flight: a second mutation while one is running is ignored', () => {
    service.setActionDone.mockReturnValueOnce(NEVER);
    component.toggleDone(action(1));
    expect(component.busy).toBe(true);
    component.toggleDone(action(2));
    component.reassign(action(1), 8);
    textDialog();
    component.withdraw(action(1));
    expect(service.setActionDone).toHaveBeenCalledTimes(1);
    expect(service.reassignAction).not.toHaveBeenCalled();
    expect(dialogOpen).not.toHaveBeenCalled();
  });

  it('withdraws with the reason from the dialog', () => {
    const dialog = textDialog();
    component.withdraw(action(1));
    dialog.save('Ikke længere relevant');
    expect(service.withdrawAction).toHaveBeenCalledWith(1, 'Ikke længere relevant');
    expect(dialog.close).toHaveBeenCalled();
    expect(changed).toHaveBeenCalledTimes(1);
    expect(component.busy).toBe(false);
  });

  it('does nothing and stays usable when the reason dialog is dismissed', () => {
    const dialog = textDialog();
    component.withdraw(action(1));
    dialog.cancel();
    expect(service.withdrawAction).not.toHaveBeenCalled();
    expect(changed).not.toHaveBeenCalled();
    expect(component.busy).toBe(false);
    component.toggleDone(action(1));
    expect(service.setActionDone).toHaveBeenCalledTimes(1);
  });

  it('is busy while the reason dialog is open', () => {
    textDialog();
    component.withdraw(action(1));
    expect(component.busy).toBe(true);
  });

  it('reloads and keeps the reason dialog open when a withdrawal is refused or fails; closing it frees the page', () => {
    service.withdrawAction.mockReturnValueOnce(of({success: false, message: 'The action is already done.'}));
    const dialog = textDialog();
    component.withdraw(action(1));
    dialog.save('Grund');
    expect(changed).toHaveBeenCalledTimes(1);
    expect(dialog.close).not.toHaveBeenCalled();
    expect(component.busy).toBe(true);
    service.withdrawAction.mockReturnValueOnce(throwError(() => new Error('boom')));
    dialog.save('Grund');
    expect(changed).toHaveBeenCalledTimes(2);
    expect(dialog.close).not.toHaveBeenCalled();
    dialog.cancel();
    expect(component.busy).toBe(false);
  });

  it('never withdraws or reassigns a done or withdrawn action', () => {
    component.withdraw(action(1, {doneAt: '2026-10-02T07:00:00'}));
    component.withdraw(action(1, {withdrawnAt: '2026-10-02T07:00:00'}));
    component.reassign(action(1, {doneAt: '2026-10-02T07:00:00'}), 8);
    component.reassign(action(1, {withdrawnAt: '2026-10-02T07:00:00'}), 8);
    expect(dialogOpen).not.toHaveBeenCalled();
    expect(service.withdrawAction).not.toHaveBeenCalled();
    expect(service.reassignAction).not.toHaveBeenCalled();
  });

  it('allows nothing once the outbreak is closed', () => {
    component.detail = {...component.detail, summary: {...component.detail.summary, closed: true}};
    expect(component.editable).toBe(false);
    expect(component.canDone(action(1))).toBe(false);
    expect(component.canChange(action(1))).toBe(false);
  });

  it('offers done on open, overdue and done actions, and change only on open and overdue ones', () => {
    expect(component.canDone(action(1))).toBe(true);
    expect(component.canDone(action(1, {doneAt: 'x'}))).toBe(true);
    expect(component.canDone(action(1, {withdrawnAt: 'x'}))).toBe(false);
    expect(component.canChange(action(1))).toBe(true);
    expect(component.canChange(action(1, {doneAt: 'x'}))).toBe(false);
  });

  it('reassigns only to a different worker', () => {
    component.reassign(action(1), 7);
    component.reassign(action(1), null);
    expect(service.reassignAction).not.toHaveBeenCalled();
    component.reassign(action(1), 8);
    expect(service.reassignAction).toHaveBeenCalledWith(1, 8);
    expect(changed).toHaveBeenCalledTimes(1);
  });

  it('shows the picked worker while the reassignment is saved and keeps it once it succeeded', () => {
    const a = component.detail.actions[1];
    expect(component.selectedSite(a)).toBe(7);
    component.reassign(a, 8);
    expect(component.selectedSite(a)).toBe(8);
  });

  it('puts the select back on the server value when a reassignment is refused or fails', () => {
    const a = component.detail.actions[1];
    service.reassignAction.mockReturnValueOnce(of({success: false, message: 'The responsible person must be a worker on this property.'}));
    component.reassign(a, 8);
    expect(component.selectedSite(a)).toBe(7);
    expect(changed).toHaveBeenCalledTimes(1);
    expect(component.busy).toBe(false);
    service.reassignAction.mockReturnValueOnce(throwError(() => new Error('boom')));
    component.reassign(a, 8);
    expect(component.selectedSite(a)).toBe(7);
    expect(changed).toHaveBeenCalledTimes(2);
  });

  it('drops the picked worker when a fresh detail arrives', () => {
    const a = component.detail.actions[1];
    component.reassign(a, 8);
    const next = {...component.detail, actions: [{...a, responsibleSiteId: 9}]};
    const previous = component.detail;
    component.detail = next;
    component.ngOnChanges({detail: new SimpleChange(previous, next, false)});
    expect(component.selectedSite(next.actions[0])).toBe(9);
  });

  it('flags a responsible person who has left the property', () => {
    expect(component.isAssignable(7)).toBe(true);
    expect(component.isAssignable(99)).toBe(false);
    expect(component.workerName(99)).toBe('#99');
  });

  it('refreshes today when a new detail arrives', () => {
    component.today = new Date(2020, 0, 1);
    component.ngOnChanges({detail: new SimpleChange(null, component.detail, false)});
    expect(component.today.getFullYear()).toBeGreaterThanOrEqual(2026);
  });

  it('leaves the select on the model value when a reassign is not allowed or is to the same worker', () => {
    const a = component.detail.actions[1];
    component.reassign(a, 8);
    component.reassign(a, 7);
    expect(component.selectedSite(a)).toBe(7);
    const done = action(1, {doneAt: 'x'});
    component.reassign(done, 8);
    expect(component.selectedSite(done)).toBe(7);
  });

  it('adds the responsible person who left as a disabled item, built once per detail', () => {
    const a = action(1, {responsibleSiteId: 99});
    const items = component.itemsOf(a);
    expect(items.map((w) => w.siteId)).toEqual([7, 8, 99]);
    expect(items[2].disabled).toBe(true);
    expect(component.itemsOf(a)).toBe(items);
    expect(component.itemsOf(action(2)).map((w) => w.siteId)).toEqual([7, 8]);
    component.workers = [...component.workers];
    expect(component.itemsOf(a)).not.toBe(items);
  });

  it('names a resigned responsible person, flags them, and offers them only as the disabled current choice', () => {
    component.workers = [...component.workers, {siteId: 9, name: 'Jane Roe', assignable: false}];
    const a = action(1, {responsibleSiteId: 9});
    expect(component.workerName(9)).toBe('Jane Roe');
    expect(component.isAssignable(9)).toBe(false);
    expect(component.itemsOf(a)).toEqual([
      {siteId: 7, name: 'Jane Doe', assignable: true}, {siteId: 8, name: 'John Doe', assignable: true},
      {siteId: 9, name: 'Jane Roe', assignable: false, disabled: true},
    ]);
    // Another action's picker does not offer the resigned worker at all.
    expect(component.itemsOf(action(2)).map((w) => w.siteId)).toEqual([7, 8]);
  });
});

describe('TailBiteFollowUpsComponent rendering', () => {
  let fixture: ComponentFixture<TailBiteFollowUpsComponent>;
  const render = (actions: TailBiteOutbreakAction[], closed = false) => {
    TestBed.configureTestingModule({
      imports: [TranslateModule.forRoot(), TailBiteFollowUpsComponent],
      providers: [
        {provide: BackendConfigurationPnTailBiteService, useValue: {}},
        {provide: MatDialog, useValue: {open: jest.fn()}},
        {provide: Overlay, useValue: {scrollStrategies: {reposition: () => ({})}}},
      ],
    });
    fixture = TestBed.createComponent(TailBiteFollowUpsComponent);
    const c = fixture.componentInstance;
    c.detail = {
      summary: {id: 5, locationId: 2, openedAt: '2026-10-01T06:12:00', assessed: true, openActions: 1, closed, bittenPigs: 3, severePigs: 1},
      ruleId: 9, ruleVersion: 1, registrationIds: [], answers: null, actions,
    };
    c.workers = [{siteId: 7, name: 'Jane Doe', assignable: true}];
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  };

  it('has column headers', () => {
    const el = render([action(1)]);
    expect(el.querySelectorAll('thead th').length).toBe(5);
  });

  it('names the responsible person who left and flags it, in an editable row', async () => {
    const el = render([action(1, {responsibleSiteId: 99})]);
    await fixture.whenStable();
    fixture.detectChanges();
    // ng-select does not paint its selected label in jsdom, so assert what the select is given: the model value and an
    // item for the person who left (disabled), which is what makes the label show in a browser.
    const select = fixture.debugElement.query(By.directive(MtxSelect));
    const items = (select.componentInstance as MtxSelect).items as {siteId: number; name: string; disabled?: boolean}[];
    expect(items.find((i) => i.siteId === 99)).toEqual(expect.objectContaining({name: '#99', disabled: true}));
    expect(select.injector.get(NgModel).model).toBe(99);
    expect(el.querySelector('#tailBiteActionLeft-1')).not.toBeNull();
  });

  it('names the responsible person who left and flags it, in a done row and a closed outbreak', () => {
    const done = render([action(1, {responsibleSiteId: 99, doneAt: '2026-10-02T07:00:00'})]);
    expect(done.querySelector('#tailBiteActionRow-1')?.textContent).toContain('#99');
    expect(done.querySelector('#tailBiteActionLeft-1')).not.toBeNull();
    TestBed.resetTestingModule();
    const closed = render([action(1, {responsibleSiteId: 99})], true);
    expect(closed.querySelector('#tailBiteActionRow-1')?.textContent).toContain('#99');
    expect(closed.querySelector('#tailBiteActionLeft-1')).not.toBeNull();
  });

  it('does not flag a responsible person who is on the property', () => {
    const el = render([action(1)]);
    expect(el.querySelector('#tailBiteActionLeft-1')).toBeNull();
  });
});
