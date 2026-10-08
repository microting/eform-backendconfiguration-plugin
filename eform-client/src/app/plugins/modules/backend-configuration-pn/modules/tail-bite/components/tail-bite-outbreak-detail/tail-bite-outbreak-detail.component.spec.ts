import {Overlay} from '@angular/cdk/overlay';
import {EventEmitter} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {MatDialog} from '@angular/material/dialog';
import {ActivatedRoute, Router, convertToParamMap} from '@angular/router';
import {TranslateService} from '@ngx-translate/core';
import {BehaviorSubject, NEVER, Subject, of, throwError} from 'rxjs';
import {BackendConfigurationPnTailBiteService} from '../../../../services';
import {TailBiteOutbreakDetailComponent} from './tail-bite-outbreak-detail.component';

describe('TailBiteOutbreakDetailComponent', () => {
  const summary = {id: 5, locationId: 2, openedAt: '2026-10-01T06:12:00', assessed: true, openActions: 0, closed: false,
    bittenPigs: 3, severePigs: 1};
  const tree = {propertyId: 3, treeVersion: 1, actionTypes: [], locations: [
    {id: 1, parentId: null, name: 'Ejendom', depth: 0, sortOrder: 0, qrCode: 'a', removed: false},
    {id: 2, parentId: 1, name: 'Stald A', depth: 1, sortOrder: 0, qrCode: 'b', removed: false},
  ]};
  const regRow = {registrationId: 21, rowId: 210, locationId: 2, effectiveAt: '2026-09-30T05:58:00Z', minor: 2, severe: 1,
    actionTypeIds: [], siteId: 7, siteName: 'Jane Doe', cancelled: false, cancelReason: null, photoCount: 0};
  let service: Record<string, jest.Mock>;
  let dialogOpen: jest.Mock;
  let navigate: jest.Mock;
  let params: BehaviorSubject<ReturnType<typeof convertToParamMap>>;
  let component: TailBiteOutbreakDetailComponent;

  const detail = (over = {}) => of({success: true, model: {summary: {...summary, ...over}, ruleId: 9, ruleVersion: 1,
    registrationIds: [21], answers: null, actions: []}});
  const confirmDialog = () => {
    const confirmed = new EventEmitter<unknown>();
    dialogOpen.mockReturnValue({componentInstance: {delete: confirmed}, close: jest.fn(), afterClosed: () => NEVER});
    return confirmed;
  };

  beforeEach(() => {
    service = {
      getOutbreak: jest.fn().mockReturnValue(detail()),
      getOutbreakRegistrations: jest.fn().mockReturnValue(of({success: true, model: {propertyId: 3, rows: [regRow]}})),
      getTree: jest.fn().mockReturnValue(of({success: true, model: tree})),
      getWorkers: jest.fn().mockReturnValue(of({success: true, model: [{siteId: 7, name: 'Jane Doe', isManager: true, propertyWorkerIds: [1]}]})),
      getOccupancy: jest.fn().mockReturnValue(of({success: true, model: []})),
      getRuleHistory: jest.fn().mockReturnValue(of({success: true, model: []})),
      closeOutbreak: jest.fn().mockReturnValue(of({success: true})),
      cancelRegistration: jest.fn().mockReturnValue(of({success: true})),
    };
    dialogOpen = jest.fn();
    navigate = jest.fn();
    params = new BehaviorSubject(convertToParamMap({propertyId: '3', id: '5'}));
    TestBed.configureTestingModule({
      providers: [
        {provide: BackendConfigurationPnTailBiteService, useValue: service},
        {provide: ActivatedRoute, useValue: {paramMap: params}},
        {provide: Router, useValue: {navigate}},
        {provide: MatDialog, useValue: {open: dialogOpen}},
        {provide: Overlay, useValue: {scrollStrategies: {reposition: () => ({})}}},
        {provide: TranslateService, useValue: {instant: (k: string) => k}},
      ],
    });
    component = TestBed.runInInjectionContext(() => new TailBiteOutbreakDetailComponent());
    component.ngOnInit();
  });

  afterEach(() => component.ngOnDestroy());

  describe('loading', () => {
    it('loads the outbreak, then the data of its property', () => {
      expect(service.getTree).toHaveBeenCalledWith(3);
      expect(service.getWorkers).toHaveBeenCalledWith(3);
      expect(service.getRuleHistory).toHaveBeenCalledWith(9);
      expect(component.view!.title).toBe('Stald A');
      expect(component.view!.bittenPigs).toBe(3);
      expect(navigate).not.toHaveBeenCalled();
    });

    it('corrects a link with another property id to the outbreak\'s own, without loading that property', () => {
      service.getTree.mockClear();
      params.next(convertToParamMap({propertyId: '4', id: '5'}));
      expect(navigate).toHaveBeenCalledWith(['/plugins/backend-configuration-pn/tail-bite', 3, 'outbreaks', 5], {replaceUrl: true});
      expect(service.getTree).not.toHaveBeenCalled();
      expect(component.view).toBeNull();
    });

    it('shows nothing when the first load is refused', () => {
      service.getOutbreak.mockReturnValue(of({success: false, message: 'Not found or no access.'}));
      params.next(convertToParamMap({propertyId: '3', id: '6'}));
      expect(component.view).toBeNull();
      expect(component.detail).toBeNull();
    });

    it('says so, with a way back, when the first load is refused or fails; not while redirecting', () => {
      service.getOutbreak.mockReturnValue(of({success: false, message: 'Not found or no access.'}));
      params.next(convertToParamMap({propertyId: '3', id: '6'}));
      expect(component.notFound).toBe(true);
      service.getOutbreak.mockReturnValue(throwError(() => new Error('down')));
      params.next(convertToParamMap({propertyId: '3', id: '7'}));
      expect(component.notFound).toBe(true);
      service.getOutbreak.mockReturnValue(detail());
      params.next(convertToParamMap({propertyId: '4', id: '5'}));
      expect(component.notFound).toBe(false);
      params.next(convertToParamMap({propertyId: '3', id: '5'}));
      expect(component.notFound).toBe(false);
      expect(component.view).not.toBeNull();
    });

    it('does not say "not found" when a refresh fails: the page is still shown', () => {
      service.getOutbreak.mockReturnValue(of({success: false, message: 'no'}));
      component.load();
      expect(component.notFound).toBe(false);
    });

    it('still shows the page when the workers, occupancy or rule history cannot be reached', () => {
      service.getWorkers.mockReturnValue(throwError(() => new Error('down')));
      service.getOccupancy.mockReturnValue(throwError(() => new Error('down')));
      service.getRuleHistory.mockReturnValue(throwError(() => new Error('down')));
      params.next(convertToParamMap({propertyId: '3', id: '6'}));
      expect(component.view!.title).toBe('Stald A');
      expect(component.workers).toEqual([]);
      expect(component.view!.rule).toBeNull();
      expect(component.view!.pigs).toBeNull();
    });

    it('keeps the page when a refresh is refused', () => {
      service.getOutbreak.mockReturnValue(of({success: false, message: 'Not found or no access.'}));
      component.load();
      expect(component.view!.title).toBe('Stald A');
    });

    it('keeps the page when a refresh fails, and when the tree is refused', () => {
      service.getOutbreakRegistrations.mockReturnValue(throwError(() => new Error('down')));
      component.load();
      expect(component.view!.title).toBe('Stald A');
      service.getOutbreakRegistrations.mockReturnValue(of({success: true, model: {propertyId: 3, rows: [regRow]}}));
      service.getTree.mockReturnValue(of({success: false, message: 'no'}));
      component.load();
      expect(component.view!.title).toBe('Stald A');
    });

    it('clears the page at once when the route switches to another outbreak, and drops the old answer', () => {
      const late = new Subject<unknown>();
      service.getOutbreak.mockReturnValueOnce(late);
      params.next(convertToParamMap({propertyId: '3', id: '6'}));
      expect(component.view).toBeNull();
      expect(component.detail).toBeNull();
      expect(component.workers).toEqual([]);
      // A newer request supersedes the one in flight, whose answer then arrives late.
      service.getOutbreak.mockReturnValue(detail({id: 7}));
      params.next(convertToParamMap({propertyId: '3', id: '7'}));
      expect(component.detail!.summary.id).toBe(7);
      late.next({success: true, model: {summary: {...summary, id: 6}, ruleId: 9, ruleVersion: 1, registrationIds: [], answers: null, actions: []}});
      late.complete();
      expect(component.detail!.summary.id).toBe(7);
    });

    it('drops an answer that arrives after the page is left', () => {
      const late = new Subject<unknown>();
      service.getOutbreak.mockReturnValue(late);
      params.next(convertToParamMap({propertyId: '3', id: '6'}));
      component.ngOnDestroy();
      late.next({success: true, model: {summary: {...summary, id: 6}, ruleId: 9, ruleVersion: 1, registrationIds: [], answers: null, actions: []}});
      expect(component.view).toBeNull();
    });

    it('does not call the server for an id that is not a number', () => {
      service.getOutbreak.mockClear();
      params.next(convertToParamMap({propertyId: '3', id: 'abc'}));
      expect(service.getOutbreak).not.toHaveBeenCalled();
      expect(component.view).toBeNull();
    });
  });

  describe('closing', () => {
    it('closes only when ready, after confirming, then reloads', () => {
      const confirmed = confirmDialog();
      expect(component.canClose).toBe(true);
      expect(component.closeBlockedReason).toBeNull();
      component.close();
      expect(service.closeOutbreak).not.toHaveBeenCalled();
      confirmed.emit({});
      expect(service.closeOutbreak).toHaveBeenCalledWith(5);
      expect(service.getOutbreak).toHaveBeenCalledTimes(2);
      expect(component.busy).toBe(false);
    });

    it('does not offer closing while follow-ups are open, and says why', () => {
      service.getOutbreak.mockReturnValue(detail({openActions: 1}));
      component.load();
      expect(component.canClose).toBe(false);
      expect(component.closeBlockedReason).toBe('Can be closed when every follow-up is done or withdrawn.');
      component.close();
      expect(dialogOpen).not.toHaveBeenCalled();
    });

    it('does not offer closing before the assessment, and says why', () => {
      service.getOutbreak.mockReturnValue(detail({assessed: false}));
      component.load();
      expect(component.canClose).toBe(false);
      expect(component.closeBlockedReason).toBe('Save the risk assessment before closing.');
      component.close();
      expect(dialogOpen).not.toHaveBeenCalled();
    });

    it('does not offer closing an outbreak that is closed, and needs no reason', () => {
      service.getOutbreak.mockReturnValue(detail({closed: true}));
      component.load();
      expect(component.canClose).toBe(false);
      expect(component.closeBlockedReason).toBeNull();
    });

    it('is single-flight while the close is running', () => {
      const confirmed = confirmDialog();
      const running = new Subject<unknown>();
      service.closeOutbreak.mockReturnValue(running);
      component.close();
      confirmed.emit({});
      expect(component.busy).toBe(true);
      component.close();
      expect(dialogOpen).toHaveBeenCalledTimes(1);
      running.next({success: true});
      running.complete();
      expect(component.busy).toBe(false);
    });

    it('reloads when the server refuses the close, frees the page and keeps the dialog usable', () => {
      const confirmed = confirmDialog();
      service.closeOutbreak.mockReturnValue(of({success: false, message: '1 follow-up action(s) are not done or withdrawn.'}));
      component.close();
      confirmed.emit({});
      expect(service.getOutbreak).toHaveBeenCalledTimes(2);
      expect(component.busy).toBe(false);
      confirmed.emit({});
      expect(service.closeOutbreak).toHaveBeenCalledTimes(2);
    });

    it('reloads and frees the page when the close request fails', () => {
      const confirmed = confirmDialog();
      service.closeOutbreak.mockReturnValue(throwError(() => new Error('down')));
      component.close();
      confirmed.emit({});
      expect(service.getOutbreak).toHaveBeenCalledTimes(2);
      expect(component.busy).toBe(false);
    });
  });

  describe('cancelling a registration', () => {
    it('does nothing when the reason dialog is dismissed, and frees the page', () => {
      dialogOpen.mockReturnValueOnce({afterClosed: () => of(undefined)});
      component.cancelRegistration(component.view!.rows[0]);
      expect(service.cancelRegistration).not.toHaveBeenCalled();
      expect(component.busy).toBe(false);
    });

    it('cancels with the reason from the text dialog, then reloads', () => {
      dialogOpen.mockReturnValueOnce({afterClosed: () => of('Registreret på forkert sti')});
      component.cancelRegistration(component.view!.rows[0]);
      expect(service.cancelRegistration).toHaveBeenCalledWith(21, 'Registreret på forkert sti');
      expect(service.getOutbreak).toHaveBeenCalledTimes(2);
      expect(component.busy).toBe(false);
    });

    it('reloads when the server refuses the cancel', () => {
      service.cancelRegistration.mockReturnValue(of({success: false, message: 'Registration not found.'}));
      dialogOpen.mockReturnValueOnce({afterClosed: () => of('Fejl')});
      component.cancelRegistration(component.view!.rows[0]);
      expect(service.getOutbreak).toHaveBeenCalledTimes(2);
      expect(component.busy).toBe(false);
    });

    it('reloads and frees the page when the cancel request fails', () => {
      service.cancelRegistration.mockReturnValue(throwError(() => new Error('down')));
      dialogOpen.mockReturnValueOnce({afterClosed: () => of('Fejl')});
      component.cancelRegistration(component.view!.rows[0]);
      expect(service.getOutbreak).toHaveBeenCalledTimes(2);
      expect(component.busy).toBe(false);
    });

    it('is single-flight: a second cancel while one is running is ignored', () => {
      const running = new Subject<unknown>();
      service.cancelRegistration.mockReturnValue(running);
      dialogOpen.mockReturnValue({afterClosed: () => of('Fejl')});
      component.cancelRegistration(component.view!.rows[0]);
      expect(component.busy).toBe(true);
      component.cancelRegistration(component.view!.rows[0]);
      expect(service.cancelRegistration).toHaveBeenCalledTimes(1);
      running.next({success: true});
      running.complete();
      expect(component.busy).toBe(false);
    });
  });
});
