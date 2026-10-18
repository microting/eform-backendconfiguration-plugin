import {DestroyRef, Injector, runInInjectionContext} from '@angular/core';
import {Overlay} from '@angular/cdk/overlay';
import {MatDialog} from '@angular/material/dialog';
import {TranslateService} from '@ngx-translate/core';
import {Store} from '@ngrx/store';
import {BehaviorSubject, EMPTY, Subject, of} from 'rxjs';
import {BackendConfigurationPnChemicalsService} from '../../../../services';
import {inventoryOf, location, placement} from '../../helpers/chemical-inventory.fixtures';
import {ChemicalLocationsPageComponent} from './chemical-locations-page.component';

describe('ChemicalLocationsPageComponent', () => {
  let chemicals: Record<string, jest.Mock>;
  let dialog: {open: jest.Mock};
  let propertyId$: BehaviorSubject<number | null>;
  /** What the location dialog closes with. */
  let dialogResult: boolean;
  /** The archive confirm: answer it with confirmed.next() or keep with dismissed.next(). */
  let confirmed: Subject<void>;
  let dismissed: Subject<void>;

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
    dialogResult = true;
    dialog = {
      open: jest.fn().mockImplementation((_component: unknown, config: {data?: {settings?: unknown}}) => {
        if (!config.data?.settings) {
          return {afterClosed: () => of(dialogResult)};
        }
        confirmed = new Subject<void>();
        dismissed = new Subject<void>();
        return {componentInstance: {delete: confirmed}, afterClosed: () => dismissed, close: jest.fn()};
      }),
    };
    propertyId$ = new BehaviorSubject<number | null>(3);
    const injector = Injector.create({providers: [
      {provide: Store, useValue: {select: jest.fn().mockReturnValue(propertyId$)}},
      {provide: BackendConfigurationPnChemicalsService, useValue: chemicals},
      {provide: MatDialog, useValue: dialog},
      {provide: Overlay, useValue: {scrollStrategies: {reposition: jest.fn().mockReturnValue({})}}},
      {provide: TranslateService, useValue: {instant: (key: string) => key}},
      {provide: DestroyRef, useValue: {onDestroy: jest.fn()}},
    ]});
    const component = runInInjectionContext(injector, () => new ChemicalLocationsPageComponent());
    component.ngOnInit();
    return component;
  }

  const ids = (component: ChemicalLocationsPageComponent) => component.visibleRows.map((r) => r.location.id);

  it('hides archived locations until asked', () => {
    const component = build();

    expect(ids(component)).toEqual([1, 2]);
    component.showArchived = true;
    expect(ids(component)).toEqual([1, 2, 3]);
    expect(component.archivedCount).toBe(1);
  });

  describe('order', () => {
    it('saves the dragged order of active locations', () => {
      const component = build();

      component.drop({previousIndex: 1, currentIndex: 0} as never);

      expect(chemicals.reorderLocations).toHaveBeenCalledWith(3, [2, 1]);
      expect(ids(component)).toEqual([2, 1]);
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
      expect(ids(component)).toEqual([2, 1, 3]);
    });

    it('lets a row be sorted only among the active locations', () => {
      const component = build();
      component.showArchived = true;

      expect(component.canSortTo(0)).toBe(true);
      expect(component.canSortTo(1)).toBe(true);
      expect(component.canSortTo(2)).toBe(false);
    });

    it('moves a row up or down by one with the buttons (keyboard and single-pointer alternative to dragging, I2)', () => {
      const component = build();
      const kemirum = component.visibleRows[1];

      component.moveUp(kemirum);
      expect(chemicals.reorderLocations).toHaveBeenLastCalledWith(3, [2, 1]);
      expect(ids(component)).toEqual([2, 1]);

      component.moveDown(kemirum);
      expect(chemicals.reorderLocations).toHaveBeenLastCalledWith(3, [1, 2]);
      expect(ids(component)).toEqual([1, 2]);
    });

    it('offers no move past the ends of the active list, and none for an archived row', () => {
      const component = build();
      component.showArchived = true;
      const [lade, kemirum, archived] = component.visibleRows;

      expect(component.canMoveUp(lade)).toBe(false);
      expect(component.canMoveDown(lade)).toBe(true);
      expect(component.canMoveUp(kemirum)).toBe(true);
      expect(component.canMoveDown(kemirum)).toBe(false);
      expect(component.canMoveUp(archived)).toBe(false);
      expect(component.canMoveDown(archived)).toBe(false);

      component.moveUp(lade);
      component.moveDown(kemirum);
      component.moveUp(archived);
      expect(chemicals.reorderLocations).not.toHaveBeenCalled();
    });

    it('blocks a second drag or move, and reports busy, until the first order is saved (M3)', () => {
      const component = build();
      const answer = new Subject<{success: boolean; model: []}>();
      chemicals.reorderLocations.mockReturnValue(answer);

      component.drop({previousIndex: 1, currentIndex: 0} as never);
      expect(component.reordering).toBe(true);
      expect(component.busy).toBe(true);
      expect(component.canMoveDown(component.visibleRows[0])).toBe(false);
      component.drop({previousIndex: 1, currentIndex: 0} as never);
      component.moveDown(component.visibleRows[0]);
      expect(chemicals.reorderLocations).toHaveBeenCalledTimes(1);

      answer.next({success: true, model: []});
      answer.complete();
      expect(component.reordering).toBe(false);
      expect(component.busy).toBe(false);
    });

    it('reloads the server order when the new order is refused', () => {
      const component = build();
      chemicals.reorderLocations.mockReturnValue(of({success: false, message: 'Ingen adgang'}));

      component.drop({previousIndex: 1, currentIndex: 0} as never);

      expect(chemicals.getPropertyInventory).toHaveBeenCalledTimes(2);
      expect(ids(component)).toEqual([1, 2]);
      expect(component.reordering).toBe(false);
    });

    it('reloads the server order when the order gets no answer at all (the host interceptor ends with EMPTY, I1)', () => {
      const component = build();
      chemicals.reorderLocations.mockReturnValue(EMPTY);

      component.drop({previousIndex: 1, currentIndex: 0} as never);

      expect(chemicals.getPropertyInventory).toHaveBeenCalledTimes(2);
      expect(ids(component)).toEqual([1, 2]);
      expect(component.reordering).toBe(false);
    });

    it('takes over the saved locations the server returns, so "Last changed" is current (M4)', () => {
      const component = build();
      chemicals.reorderLocations.mockReturnValue(of({success: true, model: [
        location(2, 'Kemirum', {propertyId: 3, sortOrder: 1, photoFileName: 'p.jpg', updatedAt: '2026-10-17T09:00:00'}),
        location(1, 'Lade', {propertyId: 3, sortOrder: 2, updatedAt: '2026-10-17T09:00:00'}),
      ]}));

      component.drop({previousIndex: 1, currentIndex: 0} as never);

      expect(ids(component)).toEqual([2, 1]);
      expect(component.visibleRows.map((r) => r.location.updatedAt)).toEqual(['2026-10-17T09:00:00', '2026-10-17T09:00:00']);
      expect(component.visibleRows[0].openCount).toBe(1);
      expect(chemicals.getPropertyInventory).toHaveBeenCalledTimes(1);
    });

    it('drops an order answer that arrives after a reload, so it cannot undo an archive', () => {
      const component = build();
      const answer = new Subject<{success: boolean; model: ReturnType<typeof location>[]}>();
      chemicals.reorderLocations.mockReturnValue(answer);
      component.drop({previousIndex: 1, currentIndex: 0} as never);

      // While the order is saving, "Lade" is archived and the page reloads it as archived.
      chemicals.getPropertyInventory.mockImplementation(() => {
        const archived = inventory();
        archived.model.locations[0] = location(1, 'Lade', {propertyId: 3, sortOrder: 1, archived: true});
        return of(archived);
      });
      component.archive(component.visibleRows.find((r) => r.location.id === 1));
      confirmed.next();
      expect(ids(component)).toEqual([2]);

      // The older order answer still carries "Lade" as active.
      answer.next({success: true, model: [
        location(2, 'Kemirum', {propertyId: 3, sortOrder: 1, photoFileName: 'p.jpg'}),
        location(1, 'Lade', {propertyId: 3, sortOrder: 2}),
      ]});
      answer.complete();

      expect(ids(component)).toEqual([2]);
      expect(component.rows.find((r) => r.location.id === 1).location.archived).toBe(true);
    });
  });

  describe('archive', () => {
    it('archives only a location without open placements', () => {
      const component = build();
      const kemirum = component.visibleRows[1];

      expect(component.canArchive(kemirum)).toBe(false);
      component.archive(kemirum);

      expect(dialog.open).not.toHaveBeenCalled();
      expect(chemicals.archiveLocation).not.toHaveBeenCalled();
    });

    it('asks first, naming the location, and archives only after the confirm (I4)', () => {
      const component = build();
      const lade = component.visibleRows[0];

      component.archive(lade);

      expect(chemicals.archiveLocation).not.toHaveBeenCalled();
      const settings = dialog.open.mock.lastCall[1].data.settings;
      expect(settings.deleteButtonId).toBe('chemicalLocationArchiveConfirm');
      expect(settings.headerText).toBe('Chemicals: Archive location?');
      // The hint is a plain sentence (left, normal weight), not a bold value without a label (N-m1).
      expect(settings.fields.map((f: {header: string; text: string}) => [f.header, f.text]))
        .toEqual([['Location', 'Lade'], ['Chemicals: Archive confirm hint', '']]);
      expect(settings.deleteButtonText).toBe('Chemicals: Archive');

      confirmed.next();

      expect(chemicals.archiveLocation).toHaveBeenCalledWith(1);
      expect(chemicals.getPropertyInventory).toHaveBeenCalledTimes(2);
    });

    it('archives nothing when the confirm is dismissed', () => {
      const component = build();

      component.archive(component.visibleRows[0]);
      dismissed.next();

      expect(chemicals.archiveLocation).not.toHaveBeenCalled();
    });

    it('does not reload when the server refuses the archive (the host toasts its message)', () => {
      const component = build();
      chemicals.archiveLocation.mockReturnValue(of({success: false, message: 'Lokationen har åbne placeringer'}));

      component.archive(component.visibleRows[0]);
      confirmed.next();

      expect(chemicals.getPropertyInventory).toHaveBeenCalledTimes(1);
    });

    it('cannot archive or edit an archived location (the server writes active locations only)', () => {
      const component = build();
      component.showArchived = true;
      const archived = component.visibleRows[2];

      expect(component.canArchive(archived)).toBe(false);
      expect(component.canEdit(archived)).toBe(false);
      component.openEdit(archived);
      component.archive(archived);
      expect(dialog.open).not.toHaveBeenCalled();
    });
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
    dialogResult = false;
    const kemirum = component.visibleRows[1];

    component.openEdit(kemirum);

    expect(dialog.open.mock.lastCall[1].data).toEqual({propertyId: 3, row: kemirum, photoUrl: '/photo/2?v=p.jpg'});
    expect(chemicals.getPropertyInventory).toHaveBeenCalledTimes(1);
  });

  it('builds a photo URL only for locations with a photo, versioned by the stored file name (M1)', () => {
    const component = build();

    expect(component.photoUrl(component.visibleRows[0])).toBeNull();
    expect(component.photoUrl(component.visibleRows[1])).toBe('/photo/2?v=p.jpg');
  });

  describe('loading', () => {
    it('shows nothing as empty while the locations load, and says when they could not be loaded', () => {
      const component = build();
      const answer = new Subject<unknown>();
      chemicals.getPropertyInventory.mockReturnValue(answer);

      component.reload();
      expect(component.load.loading).toBe(true);
      expect(component.rows).toEqual([]);

      answer.error(new Error('500'));
      expect(component.load.loading).toBe(false);
      expect(component.load.failed).toBe(true);
    });

    it('treats a refused inventory as a failed load', () => {
      const component = build();
      chemicals.getPropertyInventory.mockReturnValue(of({success: false, message: 'Ingen adgang'}));

      component.reload();

      expect(component.load.failed).toBe(true);
      expect(component.rows).toEqual([]);
    });

    it('is never stuck on loading when the inventory gets no answer at all (M2)', () => {
      const component = build();
      chemicals.getPropertyInventory.mockReturnValue(EMPTY);

      component.reload();

      expect(component.load.loading).toBe(false);
      expect(component.load.failed).toBe(true);
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
      expect(ids(component)).toEqual([1, 2]);
    });

    it('offers nothing without a property', () => {
      const component = build();
      propertyId$.next(null);

      component.openCreate();

      expect(component.rows).toEqual([]);
      expect(component.load.loading).toBe(false);
      expect(dialog.open).not.toHaveBeenCalled();
    });
  });
});
