import {HttpErrorResponse} from '@angular/common/http';
import {DestroyRef, Injector, runInInjectionContext} from '@angular/core';
import {Store} from '@ngrx/store';
import {TranslateService} from '@ngx-translate/core';
import {ToastrService} from 'ngx-toastr';
import {BehaviorSubject, EMPTY, Observable, Subject, of, throwError} from 'rxjs';
import {ChemicalHistoryEventModel} from '../../../../models';
import {BackendConfigurationPnChemicalsService} from '../../../../services';
import {
  ChemicalHistoryFiltersModel,
  chemicalsInitialState,
  chemicalsUpdateHistoryFilters,
  selectChemicalsHistoryView,
} from '../../../../state';
import {location} from '../../helpers/chemical-inventory.fixtures';
import {ChemicalHistoryPageComponent} from './chemical-history-page.component';

jest.mock('file-saver', () => ({saveAs: jest.fn()}));
import {saveAs} from 'file-saver';

type View = {propertyId: number | null; filters: ChemicalHistoryFiltersModel};

describe('ChemicalHistoryPageComponent', () => {
  let view$: BehaviorSubject<View>;
  let store: {select: jest.Mock; dispatch: jest.Mock};
  let chemicals: Record<string, jest.Mock>;
  let toastr: {error: jest.Mock};

  const page = (total = 214) => ({success: true, model: {events: [], total, users: [{userId: 5, name: 'Mette Larsen'}]}});

  function build(opts: {propertyId?: number | null; history?: Observable<unknown>; locations?: unknown[]; inventory?: Observable<unknown>} = {}): ChemicalHistoryPageComponent {
    (saveAs as unknown as jest.Mock).mockClear();
    view$ = new BehaviorSubject<View>({
      propertyId: opts.propertyId === undefined ? 3 : opts.propertyId,
      filters: {...chemicalsInitialState.historyFilters},
    });
    store = {
      select: jest.fn().mockImplementation((selector) => {
        if (selector !== selectChemicalsHistoryView) {
          throw new Error('W6 reads the property and its filters through selectChemicalsHistoryView');
        }
        return view$;
      }),
      // The reducer: the new filters for the same property.
      dispatch: jest.fn().mockImplementation((action) => view$.next({...view$.value, filters: action.payload})),
    };
    chemicals = {
      getHistory: jest.fn().mockReturnValue(opts.history ?? of(page())),
      getPropertyInventory: jest.fn().mockReturnValue(opts.inventory ?? of({success: true, model: {
        locations: opts.locations ?? [location(10, 'Lade', {propertyId: 3}), location(11, 'Gammel', {propertyId: 3, archived: true})],
      }})),
      exportHistory: jest.fn().mockReturnValue(of({blob: new Blob(['x']), fileName: 'Kemihistorik.xlsx'})),
    };
    toastr = {error: jest.fn()};
    const injector = Injector.create({providers: [
      {provide: Store, useValue: store},
      {provide: BackendConfigurationPnChemicalsService, useValue: chemicals},
      {provide: DestroyRef, useValue: {onDestroy: jest.fn()}},
      {provide: ToastrService, useValue: toastr},
      {provide: TranslateService, useValue: {instant: (k: string) => k}},
    ]});
    const component = runInInjectionContext(injector, () => new ChemicalHistoryPageComponent());
    component.now = () => new Date('2026-09-29T10:00:00Z');
    component.ngOnInit();
    return component;
  }

  it('loads page 0 of the default period and the filter lists', () => {
    const component = build();

    expect(chemicals.getHistory).toHaveBeenLastCalledWith(3, {from: '2026-08-31', to: '2026-09-29', userId: null, action: null, locationId: null, page: 0, pageSize: 25});
    expect(component.total).toBe(214);
    expect(component.userOptions).toEqual([{userId: 0, name: 'Chemicals: System user'}, {userId: 5, name: 'Mette Larsen'}]);
    // Archived locations stay in the filter: their history does.
    expect(component.locations.map((l) => l.id)).toEqual([10, 11]);
  });

  it('makes no call while no property is selected', () => {
    const component = build({propertyId: null});

    expect(chemicals.getHistory).not.toHaveBeenCalled();
    expect(chemicals.getPropertyInventory).not.toHaveBeenCalled();
    component.export();
    expect(chemicals.exportHistory).not.toHaveBeenCalled();
  });

  it('a filter change goes through the store and restarts at page 0', () => {
    const component = build();
    component.onPage({pageIndex: 3, pageSize: 25});

    component.updateFilters({action: 3});

    expect(store.dispatch).toHaveBeenCalledWith(chemicalsUpdateHistoryFilters({payload: {...chemicalsInitialState.historyFilters, action: 3}}));
    expect(chemicals.getHistory).toHaveBeenLastCalledWith(3, expect.objectContaining({action: 3, page: 0}));
    expect(component.pageIndex).toBe(0);
  });

  it('pages on the server and keeps the count while the next page loads', () => {
    const next = new Subject<unknown>();
    const component = build();
    chemicals.getHistory.mockReturnValue(next);

    component.onPage({pageIndex: 2, pageSize: 50});

    expect(chemicals.getHistory).toHaveBeenLastCalledWith(3, expect.objectContaining({page: 2, pageSize: 50}));
    expect(component.loading).toBe(true);
    expect(component.total).toBe(214);
  });

  it('a property switch reloads the locations and forgets the old users', () => {
    const component = build();
    chemicals.getHistory.mockReturnValue(new Subject<unknown>());

    view$.next({propertyId: 4, filters: {...chemicalsInitialState.historyFilters}});

    expect(chemicals.getPropertyInventory).toHaveBeenLastCalledWith(4);
    expect(chemicals.getHistory).toHaveBeenLastCalledWith(4, expect.objectContaining({page: 0}));
    expect(component.users).toEqual([]);
    expect(component.total).toBe(0);
  });

  it('drops the answer of a query the user already left', () => {
    const first = new Subject<unknown>();
    const component = build({history: first});
    chemicals.getHistory.mockReturnValue(of(page(7)));
    component.updateFilters({action: 2});

    first.next(page(999));

    expect(component.total).toBe(7);
  });

  it('shows the failed state, not an empty log, on a refusal or no answer', () => {
    const refused = build({history: of({success: false, message: 'Perioden er for lang.'})});
    expect(refused.loadFailed).toBe(true);
    expect(refused.loading).toBe(false);

    // The host interceptor ends a 5xx or a network failure with EMPTY.
    const silent = build({history: EMPTY});
    expect(silent.loadFailed).toBe(true);
    expect(silent.loading).toBe(false);
  });

  it('refuses a reversed period without calling the server', () => {
    const component = build();
    chemicals.getHistory.mockClear();

    component.updateFilters({dateFrom: '2026-09-20', dateTo: '2026-09-10'});

    expect(component.periodError).toBe(true);
    expect(component.periodErrorKey).toBe('Chemicals: Period reversed');
    expect(chemicals.getHistory).not.toHaveBeenCalled();
    component.export();
    expect(chemicals.exportHistory).not.toHaveBeenCalled();
  });

  it('refuses a period over 366 days without calling the server', () => {
    const component = build();
    chemicals.getHistory.mockClear();

    component.updateFilters({dateFrom: '2025-01-01', dateTo: '2026-09-29'});

    expect(component.periodErrorKey).toBe('Chemicals: Period too long');
    expect(chemicals.getHistory).not.toHaveBeenCalled();
  });

  it('an emptied date field falls back to the default bound and shows it again', () => {
    const component = build();
    component.updateFilters({dateFrom: '2026-09-01'});
    const input = {value: ''};

    component.setDate('dateFrom', input);

    expect(component.filters.dateFrom).toBeNull();
    expect(input.value).toBe('2026-08-31');
    expect(chemicals.getHistory).toHaveBeenLastCalledWith(3, expect.objectContaining({from: '2026-08-31'}));
  });

  it('a picked date is sent as it is', () => {
    const component = build();

    component.setDate('dateTo', {value: '2026-09-15'});

    expect(chemicals.getHistory).toHaveBeenLastCalledWith(3, expect.objectContaining({from: '2026-08-31', to: '2026-09-15'}));
  });

  it('exports the whole filtered period to Excel, ignoring the page', () => {
    const component = build();
    component.updateFilters({userId: 0});
    component.onPage({pageIndex: 2, pageSize: 50});

    component.export();

    expect(chemicals.exportHistory).toHaveBeenCalledWith(3, expect.objectContaining({from: '2026-08-31', to: '2026-09-29', userId: 0, page: 0}));
    expect(saveAs).toHaveBeenCalledWith(expect.any(Blob), 'Kemihistorik.xlsx');
    expect(component.exporting).toBe(false);
  });

  it('keeps the export busy until the file arrives, ignores a second click and locks the property picker', () => {
    const file = new Subject<unknown>();
    const component = build();
    chemicals.exportHistory.mockReturnValue(file);

    component.export();
    component.export();

    expect(chemicals.exportHistory).toHaveBeenCalledTimes(1);
    expect(component.exporting).toBe(true);
    expect(component.busy).toBe(true);
    file.next({blob: new Blob(['x']), fileName: 'Kemihistorik.xlsx'});
    file.complete();
    expect(component.exporting).toBe(false);
    expect(component.busy).toBe(false);
  });

  const failures: Array<[string, unknown, string]> = [
    ['the server\'s reason (400)', {status: 400, message: 'Perioden er højst 366 dage.'}, 'Perioden er højst 366 dage.'],
    ['a refusal (403)', {status: 403, message: null}, 'Chemicals: Export not allowed'],
    ['no answer (0)', {status: 0, message: null}, 'Export failed'],
    ['a raw HttpErrorResponse, never its technical text', new HttpErrorResponse({status: 500, url: '/api/x', error: 'boom'}), 'Export failed'],
    ['an Error, never its technical text', Object.assign(new Error('TypeError: technical detail'), {status: 500}), 'Export failed'],
  ];

  it.each(failures)('toasts %s and frees the button', (_name, error, text) => {
    const component = build();
    chemicals.exportHistory.mockReturnValue(throwError(() => error));

    component.export();

    expect(toastr.error).toHaveBeenCalledTimes(1);
    expect(toastr.error).toHaveBeenCalledWith(text);
    expect(saveAs).not.toHaveBeenCalled();
    expect(component.exporting).toBe(false);
  });

  it('formats the details through the translations', () => {
    const component = build();

    expect(component.details({
      action: 2, locationName: 'Lade', targetLocationName: 'Kemirum', amount: 5, unit: 1, note: 'Reol 1',
    } as unknown as ChemicalHistoryEventModel)).toBe('Lade → Kemirum · 5 L · Reol 1');
  });

  it('counts the default period in Copenhagen days, taken again at every load (m-2)', () => {
    const component = build();
    component.now = () => new Date('2026-09-29T22:30:00Z');

    component.updateFilters({action: 1});

    expect(chemicals.getHistory).toHaveBeenLastCalledWith(3, expect.objectContaining({from: '2026-09-01', to: '2026-09-30'}));
  });

  it('marks archived locations in the filter and lists them after the active ones (m-3)', () => {
    const component = build({locations: [
      location(11, 'Gammel', {propertyId: 3, archived: true, sortOrder: 0}),
      location(10, 'Lade', {propertyId: 3, sortOrder: 2}),
      location(12, 'Kemirum', {propertyId: 3, sortOrder: 1}),
    ]});

    expect(component.locationOptions).toEqual([
      {id: 12, name: 'Kemirum'},
      {id: 10, name: 'Lade'},
      {id: 11, name: 'Gammel (Chemicals: Archived)'},
    ]);
  });

  it('says so when the locations could not be loaded (m-8)', () => {
    const silent = build({inventory: EMPTY});
    expect(silent.locationsFailed).toBe(true);

    const loaded = build();
    expect(loaded.locationsFailed).toBe(false);
  });

  it('keeps the selected user in the list when the period has no events of theirs (m-5)', () => {
    const component = build();
    component.updateFilters({userId: 5});
    chemicals.getHistory.mockReturnValue(of({success: true, model: {events: [], total: 0, users: []}}));

    component.updateFilters({dateFrom: '2026-01-01', dateTo: '2026-01-31'});

    expect(component.userOptions).toEqual([{userId: 0, name: 'Chemicals: System user'}, {userId: 5, name: 'Mette Larsen'}]);
  });

  it('names a selected user it has never seen by id, never as a blank (m-5)', () => {
    const component = build({history: of({success: true, model: {events: [], total: 0, users: []}})});

    component.updateFilters({userId: 9});

    expect(component.userOptions.map((u) => [u.userId, u.name])).toEqual([[0, 'Chemicals: System user'], [9, '#9']]);
  });

  it('hands the user filter the same list until the users change (R1)', () => {
    const component = build();

    expect(component.userOptions).toBe(component.userOptions);
  });
});
