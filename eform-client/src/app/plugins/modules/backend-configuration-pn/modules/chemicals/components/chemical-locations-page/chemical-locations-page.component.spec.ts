import {DestroyRef, Injector, runInInjectionContext} from '@angular/core';
import {Overlay} from '@angular/cdk/overlay';
import {MatDialog} from '@angular/material/dialog';
import {Store} from '@ngrx/store';
import {BehaviorSubject, Subject, of, throwError} from 'rxjs';
import {BackendConfigurationPnChemicalsService} from '../../../../services';
import {inventoryOf, location, placement} from '../../helpers/chemical-inventory.fixtures';
import {ChemicalLocationsPageComponent} from './chemical-locations-page.component';

describe('ChemicalLocationsPageComponent', () => {
  let chemicals: Record<string, jest.Mock>;
  let dialog: {open: jest.Mock};
  let propertyId$: BehaviorSubject<number | null>;

  const inventory = () => ({success: true, model: inventoryOf({
    propertyId: 3,
    locations: [
      location(1, 'Lade', {propertyId: 3, sortOrder: 1}),
      location(2, 'Kemirum', {propertyId: 3, sortOrder: 2, photoFileName: 'p.jpg'}),
      location(3, 'Gammel', {propertyId: 3, sortOrder: 3, archived: true}),
    ],
    placements: [placement(9, 2, 100)],
  })});

  function build(): ChemicalLocationsPageComponent {
    chemicals = {
      getPropertyInventory: jest.fn().mockImplementation(() => of(inventory())),
      reorderLocations: jest.fn().mockReturnValue(of({success: true, model: []})),
      archiveLocation: jest.fn().mockReturnValue(of({success: true, model: {}})),
      locationPhotoUrl: jest.fn().mockImplementation((id: number) => `/photo/${id}`),
    };
    dialog = {open: jest.fn().mockReturnValue({afterClosed: () => of(true)})};
    propertyId$ = new BehaviorSubject<number | null>(3);
    const injector = Injector.create({providers: [
      {provide: Store, useValue: {select: jest.fn().mockReturnValue(propertyId$)}},
      {provide: BackendConfigurationPnChemicalsService, useValue: chemicals},
      {provide: MatDialog, useValue: dialog},
      {provide: Overlay, useValue: {scrollStrategies: {reposition: jest.fn().mockReturnValue({})}}},
      {provide: DestroyRef, useValue: {onDestroy: jest.fn()}},
    ]});
    const component = runInInjectionContext(injector, () => new ChemicalLocationsPageComponent());
    component.ngOnInit();
    return component;
  }

  it('hides archived locations until asked', () => {
    const component = build();

    expect(component.visibleRows.map((r) => r.location.id)).toEqual([1, 2]);
    component.showArchived = true;
    expect(component.visibleRows.map((r) => r.location.id)).toEqual([1, 2, 3]);
    expect(component.archivedCount).toBe(1);
  });

  it('saves the dragged order of active locations', () => {
    const component = build();

    component.drop({previousIndex: 1, currentIndex: 0} as never);

    expect(chemicals.reorderLocations).toHaveBeenCalledWith(3, [2, 1]);
    expect(component.visibleRows.map((r) => r.location.id)).toEqual([2, 1]);
  });

  it('ignores a drop onto the same place', () => {
    build().drop({previousIndex: 1, currentIndex: 1} as never);

    expect(chemicals.reorderLocations).not.toHaveBeenCalled();
  });

  it('keeps archived rows last and out of the order body while they are shown', () => {
    const component = build();
    component.showArchived = true;

    component.drop({previousIndex: 0, currentIndex: 2} as never);

    expect(chemicals.reorderLocations).toHaveBeenCalledWith(3, [2, 1]);
    expect(component.visibleRows.map((r) => r.location.id)).toEqual([2, 1, 3]);
  });

  it('lets a row be sorted only among the active locations', () => {
    const component = build();
    component.showArchived = true;

    expect(component.canSortTo(0)).toBe(true);
    expect(component.canSortTo(1)).toBe(true);
    expect(component.canSortTo(2)).toBe(false);
  });

  it('blocks a second drag until the first order is saved', () => {
    const component = build();
    const answer = new Subject<{success: boolean}>();
    chemicals.reorderLocations.mockReturnValue(answer);

    component.drop({previousIndex: 1, currentIndex: 0} as never);
    expect(component.reordering).toBe(true);
    component.drop({previousIndex: 1, currentIndex: 0} as never);
    expect(chemicals.reorderLocations).toHaveBeenCalledTimes(1);

    answer.next({success: true});
    answer.complete();
    expect(component.reordering).toBe(false);
  });

  it('reloads the server order when the new order is refused', () => {
    const component = build();
    chemicals.reorderLocations.mockReturnValue(of({success: false, message: 'Ingen adgang'}));

    component.drop({previousIndex: 1, currentIndex: 0} as never);

    expect(chemicals.getPropertyInventory).toHaveBeenCalledTimes(2);
    expect(component.visibleRows.map((r) => r.location.id)).toEqual([1, 2]);
    expect(component.reordering).toBe(false);
  });

  it('archives only a location without open placements', () => {
    const component = build();
    const [lade, kemirum] = component.visibleRows;

    expect(component.canArchive(kemirum)).toBe(false);
    component.archive(kemirum);
    expect(chemicals.archiveLocation).not.toHaveBeenCalled();

    component.archive(lade);
    expect(chemicals.archiveLocation).toHaveBeenCalledWith(1);
    expect(chemicals.getPropertyInventory).toHaveBeenCalledTimes(2);
  });

  it('does not reload when the server refuses the archive (the host toasts its message)', () => {
    const component = build();
    chemicals.archiveLocation.mockReturnValue(of({success: false, message: 'Lokationen har åbne placeringer'}));

    component.archive(component.visibleRows[0]);

    expect(chemicals.getPropertyInventory).toHaveBeenCalledTimes(1);
  });

  it('cannot archive or edit an archived location (the server writes active locations only)', () => {
    const component = build();
    component.showArchived = true;
    const archived = component.visibleRows[2];

    expect(component.canArchive(archived)).toBe(false);
    expect(component.canEdit(archived)).toBe(false);
    component.openEdit(archived);
    expect(dialog.open).not.toHaveBeenCalled();
  });

  it('opens the dialog for a new location and reloads after save', () => {
    const component = build();

    component.openCreate();

    expect(dialog.open).toHaveBeenCalled();
    expect(dialog.open.mock.lastCall[1].data).toEqual({propertyId: 3, row: null, photoUrl: null});
    expect(chemicals.getPropertyInventory).toHaveBeenCalledTimes(2);
  });

  it('opens the dialog with the row and its photo for an edit, and does not reload after a cancel', () => {
    const component = build();
    dialog.open.mockReturnValue({afterClosed: () => of(false)});
    const kemirum = component.visibleRows[1];

    component.openEdit(kemirum);

    expect(dialog.open.mock.lastCall[1].data).toEqual({propertyId: 3, row: kemirum, photoUrl: '/photo/2'});
    expect(chemicals.getPropertyInventory).toHaveBeenCalledTimes(1);
  });

  it('builds a photo URL only for locations with a photo', () => {
    const component = build();

    expect(component.photoUrl(component.visibleRows[0])).toBeNull();
    expect(component.photoUrl(component.visibleRows[1])).toBe('/photo/2');
  });

  it('shows nothing as empty while the locations load, and says when they could not be loaded', () => {
    const component = build();
    const answer = new Subject<unknown>();
    chemicals.getPropertyInventory.mockReturnValue(answer);

    component.reload();
    expect(component.loading).toBe(true);
    expect(component.rows).toEqual([]);

    answer.error(new Error('500'));
    expect(component.loading).toBe(false);
    expect(component.loadFailed).toBe(true);
  });

  it('treats a refused inventory as a failed load', () => {
    const component = build();
    chemicals.getPropertyInventory.mockReturnValue(of({success: false, message: 'Ingen adgang'}));

    component.reload();

    expect(component.loadFailed).toBe(true);
    expect(component.rows).toEqual([]);
  });

  it('ignores the answer for a property the user already left', () => {
    const component = build();
    const late = new Subject<unknown>();
    chemicals.getPropertyInventory.mockReturnValueOnce(late);
    propertyId$.next(4);
    propertyId$.next(5);

    late.next({success: true, model: inventoryOf({propertyId: 4, locations: [location(7, 'Sen', {propertyId: 4})]})});

    expect(component.propertyId).toBe(5);
    expect(chemicals.getPropertyInventory).toHaveBeenLastCalledWith(5);
    expect(component.visibleRows.map((r) => r.location.id)).toEqual([1, 2]);
  });

  it('offers nothing without a property', () => {
    const component = build();
    propertyId$.next(null);

    component.openCreate();

    expect(component.rows).toEqual([]);
    expect(dialog.open).not.toHaveBeenCalled();
  });
});
