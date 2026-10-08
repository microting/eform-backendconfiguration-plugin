import {Overlay} from '@angular/cdk/overlay';
import {HttpRequest, HttpResponse} from '@angular/common/http';
import {ErrorHandler} from '@angular/core';
import {ComponentFixture, TestBed} from '@angular/core/testing';
import {provideNativeDateAdapter} from '@angular/material/core';
import {MatDialog} from '@angular/material/dialog';
import {ActivatedRoute, convertToParamMap, provideRouter} from '@angular/router';
import {TranslateModule} from '@ngx-translate/core';
import {BehaviorSubject, Observable, filter, map, of} from 'rxjs';
import {DateInterceptor} from 'src/app/common/interceptors/date.interceptor';
import {BackendConfigurationPnTailBiteService} from '../../../../services';
import {toDateOnly} from '../../shared/tail-bite-dates';
import {TailBiteOutbreakDetailComponent} from './tail-bite-outbreak-detail.component';

/**
 * The outbreak page as the real app sees it. The host app's global DateInterceptor turns every ISO date-time string in a
 * response into a Date before the plugin gets it, so the stubbed responses below go through that same interceptor.
 * The other specs feed strings straight in and never met a Date; this one broke on `followUpDate.substring`.
 */
describe('TailBiteOutbreakDetailComponent behind the host DateInterceptor', () => {
  const interceptor = new DateInterceptor();
  /** A server answer as JSON (camelCase, ISO strings), read back through the real interceptor. */
  const served = <T>(model: T): Observable<{success: true; model: T}> => {
    const body = JSON.parse(JSON.stringify({success: true, model}));
    return interceptor.intercept(new HttpRequest('GET', '/api/tail-bite'), {handle: () => of(new HttpResponse({body}))}).pipe(
      filter((event): event is HttpResponse<{success: true; model: T}> => event instanceof HttpResponse),
      map((event) => event.body!),
    );
  };

  const today = toDateOnly(new Date());
  const detail = {
    // The foundation's outbreak endpoints send offset-less timestamps; the web endpoints add a Z.
    summary: {id: 5, locationId: 2, openedAt: '2026-10-01T06:12:00', assessed: true, openActions: 1, closed: false,
      bittenPigs: 3, severePigs: 1},
    ruleId: 9, ruleVersion: 1, registrationIds: [21],
    answers: {water: false, feed: false, activityMaterial: false, climate: true, health: false, management: false},
    actions: [{id: 41, factor: 3, description: 'Check the vent', responsibleSiteId: 7, followUpDate: `${today}T00:00:00`,
      doneAt: null, withdrawnAt: null}],
  };
  const registrations = {propertyId: 3, actionTypes: [], rows: [{registrationId: 21, rowId: 210, locationId: 2,
    effectiveAt: '2026-09-30T05:58:00Z', minor: 2, severe: 1, actionTypeIds: [], siteId: 7, siteName: 'Jane Doe',
    cancelled: false, cancelReason: null, photoCount: 0}]};
  const tree = {propertyId: 3, treeVersion: 1, actionTypes: [], locations: [
    {id: 1, parentId: null, name: 'Farm', depth: 0, sortOrder: 0, qrCode: 'a', removed: false},
    {id: 2, parentId: 1, name: 'Barn A', depth: 1, sortOrder: 0, qrCode: 'b', removed: false},
  ]};
  const workers = [{siteId: 7, name: 'Jane Doe', assignable: true}];
  const occupancy = [{locationId: 2, pigCount: 30, source: 0, validFrom: '2026-09-15T00:00:00Z'}];
  const history = [{version: 1, locationId: 2, minBittenPigs: 3, minSevere: null, windowDays: 7, countDepth: 1,
    changedAt: '2026-08-12T10:00:00Z'}];

  let fixture: ComponentFixture<TailBiteOutbreakDetailComponent>;
  let errors: unknown[];

  beforeEach(() => {
    errors = [];
    TestBed.configureTestingModule({
      imports: [TranslateModule.forRoot(), TailBiteOutbreakDetailComponent],
      providers: [
        provideRouter([]),
        provideNativeDateAdapter(),
        {provide: ActivatedRoute, useValue: {paramMap: new BehaviorSubject(convertToParamMap({propertyId: '3', id: '5'}))}},
        {provide: BackendConfigurationPnTailBiteService, useValue: {
          getOutbreak: () => served(detail),
          getOutbreakRegistrations: () => served(registrations),
          getTree: () => served(tree),
          getAssignableWorkers: () => served(workers),
          getOccupancy: () => served(occupancy),
          getRuleHistory: () => served(history),
        }},
        {provide: MatDialog, useValue: {open: jest.fn()}},
        {provide: Overlay, useValue: {scrollStrategies: {reposition: () => ({})}}},
        {provide: ErrorHandler, useValue: {handleError: (e: unknown) => errors.push(e)}},
      ],
    });
    fixture = TestBed.createComponent(TailBiteOutbreakDetailComponent);
  });

  it('hands the page Dates, not strings, for the server dates (the setup this spec exists for)', () => {
    let model: typeof detail | undefined;
    served(detail).subscribe((res) => (model = res.model));
    expect(model!.summary.openedAt).toBeInstanceOf(Date);
    expect(model!.actions[0].followUpDate).toBeInstanceOf(Date);
    expect((model!.actions[0].followUpDate as unknown as Date).toISOString()).toBe(`${today}T00:00:00.000Z`);
  });

  it('renders the whole page, with the Done button of the open action due today, without an error', () => {
    expect(() => fixture.detectChanges()).not.toThrow();
    expect(() => fixture.detectChanges()).not.toThrow();
    expect(errors).toEqual([]);
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('#tailBiteActionDone-41')).not.toBeNull();
    expect(el.querySelector('#tailBiteActionState-41')!.textContent!.trim()).toBe('Follow-up');
    expect(el.querySelectorAll('#tailBiteRegistrationsTable tr[mat-row]').length).toBe(1);
    expect(el.querySelector('#tailBiteOutbreakRuleLine')).not.toBeNull();
    // The instants are the server's, whatever the time zone this runs in.
    const view = fixture.componentInstance.view!;
    expect(view.openedAt!.toISOString()).toBe('2026-10-01T06:12:00.000Z');
    expect(view.rows[0].effectiveAt!.toISOString()).toBe('2026-09-30T05:58:00.000Z');
    expect(view.pigsFrom!.toISOString()).toBe('2026-09-15T00:00:00.000Z');
  });
});
