import {DestroyRef, Injector, runInInjectionContext} from '@angular/core';
import {Overlay} from '@angular/cdk/overlay';
import {MatDialog} from '@angular/material/dialog';
import {ActivatedRoute, Router, convertToParamMap} from '@angular/router';
import {Store} from '@ngrx/store';
import {TranslateService} from '@ngx-translate/core';
import {ToastrService} from 'ngx-toastr';
import {BehaviorSubject, Observable, Subject, of, throwError} from 'rxjs';
import {BackendConfigurationPnChemicalsService} from '../../../../services';
import {
  chemicalsInitialState,
  chemicalsSelectProperty,
  chemicalsUpdateInventoryFilters,
  selectChemicalsInventoryFilters,
} from '../../../../state';
import {inventoryOf, location, placement} from '../../helpers/chemical-inventory.fixtures';
import {ChemicalInventoryPageComponent} from './chemical-inventory-page.component';

jest.mock('file-saver', () => ({saveAs: jest.fn()}));
import {saveAs} from 'file-saver';

describe('ChemicalInventoryPageComponent', () => {
  let property$: BehaviorSubject<number | null>;
  let filters$: BehaviorSubject<typeof chemicalsInitialState.inventoryFilters>;
  let store: {select: jest.Mock; dispatch: jest.Mock};
  let chemicals: Record<string, jest.Mock>;
  let toastr: {error: jest.Mock};
  let router: {navigate: jest.Mock; currentNavigation: jest.Mock};

  const inventoryFor = (stockEnabled: boolean) => inventoryOf({
    propertyId: 3, stockEnabled, locations: [location(10, 'Lade', {propertyId: 3})],
    placements: [7, 8].map((id) => placement(id, 10, id * 10, {propertyId: 3})),
  });

  function build(
    queryParams: Record<string, string> = {},
    opts: {stockEnabled?: boolean; propertyId?: number | null; inventory?: Observable<unknown>} = {},
  ): ChemicalInventoryPageComponent {
    (saveAs as unknown as jest.Mock).mockClear();
    property$ = new BehaviorSubject<number | null>(opts.propertyId === undefined ? 3 : opts.propertyId);
    filters$ = new BehaviorSubject({...chemicalsInitialState.inventoryFilters});
    store = {
      select: jest.fn().mockImplementation((selector) => (selector === selectChemicalsInventoryFilters ? filters$ : property$)),
      dispatch: jest.fn(),
    };
    chemicals = {
      getPropertyInventory: jest.fn().mockReturnValue(opts.inventory ?? of({success: true, model: inventoryFor(!!opts.stockEnabled)})),
      exportInventory: jest.fn().mockReturnValue(of({blob: new Blob(['x']), fileName: 'Kemikalier.xlsx'})),
      exportAuthorityList: jest.fn().mockReturnValue(of({blob: new Blob(['x']), fileName: 'Kemikalieliste.pdf'})),
      exportRemovedPlacements: jest.fn().mockReturnValue(of({blob: new Blob(['x']), fileName: 'Fjernede.xlsx'})),
    };
    toastr = {error: jest.fn()};
    router = {navigate: jest.fn().mockResolvedValue(true), currentNavigation: jest.fn().mockReturnValue(null)};
    const injector = Injector.create({providers: [
      {provide: Store, useValue: store},
      {provide: BackendConfigurationPnChemicalsService, useValue: chemicals},
      {provide: ActivatedRoute, useValue: {snapshot: {queryParamMap: convertToParamMap(queryParams)}}},
      {provide: MatDialog, useValue: {open: jest.fn()}},
      {provide: Overlay, useValue: {scrollStrategies: {reposition: jest.fn().mockReturnValue({})}}},
      {provide: DestroyRef, useValue: {onDestroy: jest.fn()}},
      {provide: ToastrService, useValue: toastr},
      {provide: Router, useValue: router},
      {provide: TranslateService, useValue: {instant: (k: string) => k, stream: (k: string) => of(k)}},
    ]});
    const component = runInInjectionContext(injector, () => new ChemicalInventoryPageComponent());
    component.ngOnInit();
    return component;
  }

  it('loads the inventory of the selected property', () => {
    const component = build();

    expect(chemicals.getPropertyInventory).toHaveBeenCalledWith(3);
    expect(component.rows.map((r) => r.placement.id)).toEqual([7, 8]);
  });

  it('makes no call while no property is selected', () => {
    const component = build({}, {propertyId: null});

    expect(chemicals.getPropertyInventory).not.toHaveBeenCalled();
    expect(component.rows).toEqual([]);
  });

  it('reloads when the property changes', () => {
    build();
    chemicals.getPropertyInventory.mockClear();

    property$.next(4);

    expect(chemicals.getPropertyInventory).toHaveBeenCalledWith(4);
  });

  it('honours the requires-action filter of the digest deeplink (Plan D C4)', () => {
    build({propertyId: '12', filter: 'requires-action'});

    expect(store.dispatch).toHaveBeenCalledWith(chemicalsUpdateInventoryFilters({
      payload: {...chemicalsInitialState.inventoryFilters, requiresActionOnly: true},
    }));
  });

  it('leaves ?propertyId= to the container, which checks it against the known properties (m-1)', () => {
    build({propertyId: '12', filter: 'requires-action'});

    expect(store.dispatch).not.toHaveBeenCalledWith(expect.objectContaining({type: chemicalsSelectProperty.type}));
  });

  describe('loading and failure (I-1)', () => {
    it('is loading, with no rows and nothing exportable, until the inventory answers', () => {
      const pending = new Subject<unknown>();
      const component = build({}, {inventory: pending});

      expect(component.loading).toBe(true);
      expect(component.loadFailed).toBe(false);
      component.exportInventory();
      expect(chemicals.exportInventory).not.toHaveBeenCalled();

      pending.next({success: true, model: inventoryFor(false)});

      expect(component.loading).toBe(false);
      expect(component.rows).toHaveLength(2);
    });

    it.each([
      ['an unsuccessful answer', () => of({success: false, message: 'x'})],
      ['a failed request', () => throwError(() => new Error('500'))],
    ])('shows the failed state after %s', (_name, answer) => {
      const component = build({}, {inventory: answer()});

      expect(component.loading).toBe(false);
      expect(component.loadFailed).toBe(true);
      expect(component.inventory).toBeNull();
      component.exportInventory();
      expect(chemicals.exportInventory).not.toHaveBeenCalled();
    });

    it('recovers on the next property', () => {
      const component = build({}, {inventory: throwError(() => new Error('500'))});
      chemicals.getPropertyInventory.mockReturnValue(of({success: true, model: inventoryFor(false)}));

      property$.next(4);

      expect(component.loadFailed).toBe(false);
      expect(component.rows).toHaveLength(2);
    });
  });

  describe('late answers (m-3)', () => {
    it('ignores the answer for a property the user already left', () => {
      const late = new Subject<unknown>();
      const component = build({}, {inventory: late});
      const other = inventoryFor(false);
      other.placements = other.placements.slice(0, 1);
      chemicals.getPropertyInventory.mockReturnValue(of({success: true, model: other}));
      property$.next(4);

      late.next({success: true, model: inventoryFor(false)});

      expect(component.rows.map((r) => r.placement.id)).toEqual([7]);
    });

    it('ignores an older answer for the same property that lands after a newer reload', () => {
      const first = new Subject<unknown>();
      const component = build({}, {inventory: first});
      const fresh = inventoryFor(false);
      fresh.placements = fresh.placements.slice(1);
      chemicals.getPropertyInventory.mockReturnValue(of({success: true, model: fresh}));
      component.reload();

      first.next({success: true, model: inventoryFor(false)});

      expect(component.rows.map((r) => r.placement.id)).toEqual([8]);
    });
  });

  describe('switching the requires-action chip off drops ?filter= (m-2)', () => {
    const requiresActionOn = () => filters$.next({...chemicalsInitialState.inventoryFilters, requiresActionOnly: true});
    const dropFilter = {queryParams: {filter: null}, queryParamsHandling: 'merge', replaceUrl: true};

    it('removes the deeplink filter from the URL', () => {
      const component = build({filter: 'requires-action'});
      requiresActionOn();

      component.toggleRequiresAction();

      expect(router.navigate).toHaveBeenCalledWith([], dropFilter);
    });

    it('never cancels a navigation in flight to do so (R-2)', () => {
      const component = build({filter: 'requires-action'});
      requiresActionOn();
      router.currentNavigation.mockReturnValue({id: 2});

      component.toggleRequiresAction();

      expect(router.navigate).not.toHaveBeenCalled();
    });

    it('leaves the URL alone when switching on, or when there is no filter parameter', () => {
      build({filter: 'requires-action'}).toggleRequiresAction();
      expect(router.navigate).not.toHaveBeenCalled();

      const component = build();
      requiresActionOn();
      component.toggleRequiresAction();
      expect(router.navigate).not.toHaveBeenCalled();
    });
  });

  it('shows the Lager column only when stock is enabled for the property (spec §11)', () => {
    expect(build({}, {stockEnabled: false}).columns.map((c) => c.field)).not.toContain('stock');
    expect(build({}, {stockEnabled: true}).columns.map((c) => c.field)).toContain('stock');
  });

  it('exports exactly the rows the table shows', () => {
    const component = build();
    filters$.next({...chemicalsInitialState.inventoryFilters, search: 'no match at all'});

    component.exportInventory();

    expect(chemicals.exportInventory).toHaveBeenCalledWith(3, []);
    expect(saveAs).toHaveBeenCalledWith(expect.any(Blob), 'Kemikalier.xlsx');
  });

  it('saves the authority list and the removed placements under the server file names', () => {
    const component = build();

    component.exportAuthorityList();
    component.exportRemovedPlacements();

    expect(saveAs).toHaveBeenCalledWith(expect.any(Blob), 'Kemikalieliste.pdf');
    expect(saveAs).toHaveBeenCalledWith(expect.any(Blob), 'Fjernede.xlsx');
  });

  const exports: Array<[string, 'exportAuthorityList' | 'exportInventory' | 'exportRemovedPlacements']> = [
    ['authority list', 'exportAuthorityList'],
    ['current filter', 'exportInventory'],
    ['removed placements', 'exportRemovedPlacements'],
  ];

  it.each(exports)('keeps the %s export busy until the file arrives, and ignores a second click', (_name, method) => {
    const component = build();
    const file$ = new Subject<{blob: Blob; fileName: string}>();
    chemicals[method].mockReturnValue(file$);

    component[method]();
    component[method]();

    expect(component.exporting).toBe(true);
    expect(chemicals[method]).toHaveBeenCalledTimes(1);

    file$.next({blob: new Blob(['x']), fileName: 'f'});
    file$.complete();

    expect(component.exporting).toBe(false);
  });

  it.each(exports)('toasts a localized failure for the %s export and frees the button (m-2)', (_name, method) => {
    const component = build();
    chemicals[method].mockReturnValue(throwError(() => new Error('500')));

    component[method]();

    expect(toastr.error).toHaveBeenCalledWith('Export failed');
    expect(component.exporting).toBe(false);
    expect(saveAs).not.toHaveBeenCalled();
  });

  it('toasts when the safety data sheet cannot be fetched (the host shows nothing for a blob error)', () => {
    const component = build();
    chemicals.getSdsBlob = jest.fn().mockReturnValue(throwError(() => new Error('404')));
    const row = {...component.rows[0], entry: {products: [{productId: 1, sdsFileName: 'sds200'}]}} as unknown as typeof component.rows[0];

    component.openSds(row);

    expect(chemicals.getSdsBlob).toHaveBeenCalledWith('sds200');
    expect(toastr.error).toHaveBeenCalledWith('Chemicals: Could not open safety data sheet');
  });

  it('shows the server\'s reason for a refused export (m-9)', () => {
    const component = build();
    chemicals.exportInventory.mockReturnValue(throwError(() => ({status: 400, message: 'Placeringen hører til en anden ejendom.'})));

    component.exportInventory();

    expect(toastr.error).toHaveBeenCalledWith('Placeringen hører til en anden ejendom.');
  });

  it('says the user may not export when the server answers 403 (m-9)', () => {
    const component = build();
    chemicals.exportAuthorityList.mockReturnValue(throwError(() => ({status: 403, message: null})));

    component.exportAuthorityList();

    expect(toastr.error).toHaveBeenCalledWith('Chemicals: Export not allowed');
  });

  it('toggles the requires-action chip through the store', () => {
    const component = build();

    component.toggleRequiresAction();

    expect(store.dispatch).toHaveBeenCalledWith(chemicalsUpdateInventoryFilters({
      payload: {...chemicalsInitialState.inventoryFilters, requiresActionOnly: true},
    }));
  });
});
