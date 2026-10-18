import {HttpErrorResponse} from '@angular/common/http';
import {DestroyRef, Injector, runInInjectionContext} from '@angular/core';
import {Overlay} from '@angular/cdk/overlay';
import {MatDialog} from '@angular/material/dialog';
import {ActivatedRoute, Router, convertToParamMap} from '@angular/router';
import {Store} from '@ngrx/store';
import {TranslateService} from '@ngx-translate/core';
import {ToastrService} from 'ngx-toastr';
import {BehaviorSubject, EMPTY, Observable, Subject, of, throwError} from 'rxjs';
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
      // The host interceptor gives up on a 5xx or a network failure with EMPTY (Task 14 M2).
      ['no answer at all', () => EMPTY],
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

  it('shows the server\'s (English) reason for a refused export after the localized prefix (m-9, E3 m-1)', () => {
    const component = build();
    chemicals.exportInventory.mockReturnValue(throwError(() => ({status: 400, message: 'The placement belongs to another property.'})));

    component.exportInventory();

    expect(toastr.error).toHaveBeenCalledWith('Export failed: The placement belongs to another property.');
  });

  it('says the user may not export when the server answers 403 (m-9)', () => {
    const component = build();
    chemicals.exportAuthorityList.mockReturnValue(throwError(() => ({status: 403, message: null})));

    component.exportAuthorityList();

    expect(toastr.error).toHaveBeenCalledWith('Chemicals: Export not allowed');
  });

  it.each([400, 500])('never shows the technical message of a raw HTTP %p error (N-m4)', (status) => {
    const component = build();
    chemicals.exportInventory.mockReturnValue(throwError(() => new HttpErrorResponse({status, url: '/api/x', error: 'boom'})));

    component.exportInventory();

    expect(toastr.error).toHaveBeenCalledWith('Export failed');
    expect(toastr.error).not.toHaveBeenCalledWith(expect.stringContaining('Http failure'));
  });

  it('never shows the message of a plain Error, even one that carries a status (N-m4)', () => {
    const component = build();
    chemicals.exportInventory.mockReturnValue(throwError(() => Object.assign(new Error('TypeError: technical detail'), {status: 500})));

    component.exportInventory();

    expect(toastr.error).toHaveBeenCalledWith('Export failed');
  });

  describe('W2 side panel and placement actions', () => {
    const dialogOf = (component: ChemicalInventoryPageComponent) =>
      (component as unknown as {dialog: {open: jest.Mock}}).dialog;

    it('opens the side panel and chains the chosen action into its dialog', () => {
      const component = build();
      const dialog = dialogOf(component);
      dialog.open
        .mockReturnValueOnce({afterClosed: () => of({action: 'remove', row: component.rows[0]})})
        .mockReturnValueOnce({afterClosed: () => of(true)});

      component.openDetails(component.rows[0]);

      expect(dialog.open).toHaveBeenCalledTimes(2);
      expect(dialog.open.mock.calls[1][0].name).toBe('ChemicalRemoveDialogComponent');
      expect(chemicals.getPropertyInventory).toHaveBeenCalledTimes(2);
    });

    it('gives the side panel the row, the inventory and the stock flag', () => {
      const component = build({}, {stockEnabled: true});
      const dialog = dialogOf(component);
      dialog.open.mockReturnValue({afterClosed: () => of(undefined)});

      component.openDetails(component.rows[0]);

      expect(dialog.open.mock.calls[0][0].name).toBe('ChemicalProductDrawerComponent');
      expect(dialog.open.mock.calls[0][1].data).toEqual({row: component.rows[0], inventory: component.inventory, stockEnabled: true});
      expect(dialog.open).toHaveBeenCalledTimes(1);
      expect(chemicals.getPropertyInventory).toHaveBeenCalledTimes(1);
    });

    it('lets Esc and a backdrop click close the read-only side panel, and names it after the product (M6, M7)', () => {
      const component = build();
      const dialog = dialogOf(component);
      dialog.open.mockReturnValue({afterClosed: () => of(undefined)});

      component.openDetails(component.rows[0]);

      const config = dialog.open.mock.calls[0][1];
      expect(config.disableClose).toBe(false);
      expect(config.ariaLabel).toBe(component.rows[0].productName);
    });

    it('keeps the placement dialogs closable only through their buttons', () => {
      const component = build();
      const dialog = dialogOf(component);
      dialog.open.mockReturnValue({afterClosed: () => of(false)});

      component.openNote(component.rows[0]);

      expect(dialog.open.mock.calls[0][1].disableClose).toBe(true);
    });

    it.each([
      ['move', 'ChemicalMoveDialogComponent'],
      ['stock', 'ChemicalStockDialogComponent'],
    ])('chains "%s" from the side panel into %s', (action, dialogName) => {
      const component = build({}, {stockEnabled: true});
      const dialog = dialogOf(component);
      dialog.open
        .mockReturnValueOnce({afterClosed: () => of({action, row: component.rows[1]})})
        .mockReturnValueOnce({afterClosed: () => of(false)});

      component.openDetails(component.rows[0]);

      expect(dialog.open.mock.calls[1][0].name).toBe(dialogName);
      expect(dialog.open.mock.calls[1][1].data.row).toBe(component.rows[1]);
    });

    it.each([
      ['openMove', 'ChemicalMoveDialogComponent'],
      ['openNote', 'ChemicalNoteDialogComponent'],
      ['openStock', 'ChemicalStockDialogComponent'],
      ['openRemove', 'ChemicalRemoveDialogComponent'],
    ] as const)('%s reloads the inventory only after a save', (method, dialogName) => {
      const component = build();
      const dialog = dialogOf(component);
      dialog.open.mockReturnValue({afterClosed: () => of(false)});

      component[method](component.rows[0]);

      expect(dialog.open.mock.calls[0][0].name).toBe(dialogName);
      expect(dialog.open.mock.calls[0][1].data.row).toBe(component.rows[0]);
      expect(chemicals.getPropertyInventory).toHaveBeenCalledTimes(1);

      dialog.open.mockReturnValue({afterClosed: () => of(true)});
      component[method](component.rows[0]);

      expect(chemicals.getPropertyInventory).toHaveBeenCalledTimes(2);
    });

    it('gives the move dialog the property\'s locations and the stock flag, and the remove dialog today', () => {
      const component = build({}, {stockEnabled: true});
      const dialog = dialogOf(component);
      dialog.open.mockReturnValue({afterClosed: () => of(false)});

      component.openMove(component.rows[0]);
      component.openRemove(component.rows[0]);

      expect(dialog.open.mock.calls[0][1].data).toEqual({row: component.rows[0], locations: component.inventory.locations, stockEnabled: true});
      expect(dialog.open.mock.calls[1][1].data.today).toMatch(/^\d{4}-\d{2}-\d{2}$/);
    });
  });

  it('toggles the requires-action chip through the store', () => {
    const component = build();

    component.toggleRequiresAction();

    expect(store.dispatch).toHaveBeenCalledWith(chemicalsUpdateInventoryFilters({
      payload: {...chemicalsInitialState.inventoryFilters, requiresActionOnly: true},
    }));
  });
});
