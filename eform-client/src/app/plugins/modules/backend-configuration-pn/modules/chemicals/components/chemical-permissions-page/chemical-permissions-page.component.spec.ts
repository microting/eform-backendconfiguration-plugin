import {DestroyRef, Injector, runInInjectionContext} from '@angular/core';
import {Store} from '@ngrx/store';
import {BehaviorSubject, of, Subject, throwError} from 'rxjs';
import {BackendConfigurationPnChemicalsService, BackendConfigurationPnPropertiesService} from '../../../../services';
import {NO_CHEMICAL_FLAGS} from '../../helpers';
import {ChemicalPermissionsPageComponent} from './chemical-permissions-page.component';

describe('ChemicalPermissionsPageComponent', () => {
  const flags = (partial: Partial<typeof NO_CHEMICAL_FLAGS>) => ({...NO_CHEMICAL_FLAGS, ...partial});
  let property$: BehaviorSubject<number | null>;
  let chemicals: {getPermissions: jest.Mock; savePermissions: jest.Mock};
  let component: ChemicalPermissionsPageComponent;

  function build(initialPropertyId: number | null = 3): ChemicalPermissionsPageComponent {
    property$ = new BehaviorSubject<number | null>(initialPropertyId);
    const injector = Injector.create({
      providers: [
        {provide: Store, useValue: {select: jest.fn().mockReturnValue(property$)}},
        {provide: BackendConfigurationPnChemicalsService, useValue: chemicals},
        {provide: BackendConfigurationPnPropertiesService, useValue: {
          getAllPropertiesDictionary: jest.fn().mockReturnValue(of({success: true, model: [{id: 3, name: 'Hovedgården'}, {id: 4, name: 'Engvej 12'}]})),
        }},
        {provide: DestroyRef, useValue: {onDestroy: jest.fn()}},
      ],
    });
    const page = runInInjectionContext(injector, () => new ChemicalPermissionsPageComponent());
    page.ngOnInit();
    return page;
  }

  beforeEach(() => {
    chemicals = {
      getPermissions: jest.fn().mockImplementation((propertyId: number) => of({
        success: true,
        model: propertyId === 3
          ? [{workerId: 1, workerName: 'Jens Hansen', flags: flags({view: true})}, {workerId: 2, workerName: 'Ole Nielsen', flags: flags({})}]
          : [{workerId: 2, workerName: 'Ole Nielsen', flags: flags({view: true, register: true})}],
      })),
      savePermissions: jest.fn().mockReturnValue(of({
        success: true,
        model: [{workerId: 1, workerName: 'Jens Hansen', flags: flags({view: true, stock: true})}, {workerId: 2, workerName: 'Ole Nielsen', flags: flags({})}],
      })),
    };
    component = build();
  });

  it('loads the matrix of the selected property', () => {
    expect(chemicals.getPermissions).toHaveBeenCalledWith(3);
    expect(component.rows.map((r) => r.workerName)).toEqual(['Jens Hansen', 'Ole Nielsen']);
    expect(component.changeCount).toBe(0);
  });

  it('offers every OTHER property in the copy menu', () => {
    expect(component.otherProperties.map((p) => p.id)).toEqual([4]);
  });

  it('saves only the changed workers and adopts the server answer', () => {
    component.toggle(component.rows[0], 'stock');
    expect(component.changeCount).toBe(1);

    component.save();

    expect(chemicals.savePermissions).toHaveBeenCalledWith(3, [{workerId: 1, flags: flags({view: true, stock: true})}]);
    expect(component.changeCount).toBe(0);
    expect(component.saving).toBe(false);
  });

  it('does not call the server when nothing changed', () => {
    component.save();

    expect(chemicals.savePermissions).not.toHaveBeenCalled();
  });

  it('copies from another property as unsaved changes', () => {
    component.copyFrom(4);

    expect(chemicals.getPermissions).toHaveBeenCalledWith(4);
    expect(component.changeCount).toBe(1);
    expect(component.isChanged(component.rows[1], 'register')).toBe(true);
  });

  it('switching property drops unsaved changes and reloads', () => {
    component.toggle(component.rows[0], 'stock');

    property$.next(4);

    expect(chemicals.getPermissions).toHaveBeenLastCalledWith(4);
    expect(component.changeCount).toBe(0);
  });

  it('filters rows by worker name, case-insensitively', () => {
    component.search = 'ole';

    expect(component.visibleRows.map((r) => r.workerId)).toEqual([2]);
  });

  it('locks the implied flags of an Admin row and unlocks them when Admin is removed', () => {
    component.toggle(component.rows[0], 'admin');
    expect(component.isImplied(component.rows[0], 'stock')).toBe(true);
    expect(component.isChecked(component.rows[0], 'stock')).toBe(true);

    component.toggle(component.rows[0], 'stock');
    expect(component.rows[0].current.stock).toBe(false);

    component.toggle(component.rows[0], 'admin');
    expect(component.isImplied(component.rows[0], 'stock')).toBe(false);
    expect(component.isChecked(component.rows[0], 'stock')).toBe(false);
    expect(component.changeCount).toBe(0);
  });

  describe('without a property (M-9: the tenant has none, the outlet still renders)', () => {
    beforeEach(() => {
      chemicals.getPermissions.mockClear();
      component = build(null);
    });

    it('makes no permissions call and shows no rows', () => {
      expect(chemicals.getPermissions).not.toHaveBeenCalled();
      expect(component.propertyId).toBeNull();
      expect(component.rows).toEqual([]);
      expect(component.loadFailed).toBe(false);
    });

    it('neither saves nor copies', () => {
      component.save();
      component.copyFrom(4);

      expect(chemicals.savePermissions).not.toHaveBeenCalled();
      expect(chemicals.getPermissions).not.toHaveBeenCalled();
    });

    it('clears the matrix when the property goes away', () => {
      component = build(3);
      expect(component.rows.length).toBe(2);

      property$.next(null);

      expect(component.rows).toEqual([]);
    });
  });

  describe('when the server refuses a save (#1409: show its message, keep the edits)', () => {
    it('shows the server message and keeps the unsaved changes for a retry', () => {
      chemicals.savePermissions.mockReturnValue(of({
        success: false,
        message: 'Fejl ved gem af kemi-rettigheder: Worker 1 is not assigned to property 3.',
      }));
      component.toggle(component.rows[0], 'stock');

      component.save();

      expect(component.saveError).toBe('Fejl ved gem af kemi-rettigheder: Worker 1 is not assigned to property 3.');
      expect(component.saveFailed).toBe(true);
      expect(component.changeCount).toBe(1);
      expect(component.saving).toBe(false);
    });

    it('flags a failed request without a server message and keeps the changes', () => {
      chemicals.savePermissions.mockReturnValue(throwError(() => new Error('offline')));
      component.toggle(component.rows[0], 'stock');

      component.save();

      expect(component.saveFailed).toBe(true);
      expect(component.saveError).toBe('');
      expect(component.changeCount).toBe(1);
      expect(component.saving).toBe(false);
    });

    it('clears the error on the next successful save and on undo', () => {
      chemicals.savePermissions.mockReturnValueOnce(of({success: false, message: 'Nope'}));
      component.toggle(component.rows[0], 'stock');
      component.save();
      expect(component.saveFailed).toBe(true);

      component.save();
      expect(component.saveFailed).toBe(false);
      expect(component.saveError).toBe('');

      chemicals.savePermissions.mockReturnValueOnce(of({success: false, message: 'Nope'}));
      component.toggle(component.rows[0], 'register');
      component.save();
      component.undo();
      expect(component.saveFailed).toBe(false);
    });
  });

  it('shows a load error instead of "no workers" when the matrix cannot be read', () => {
    chemicals.getPermissions.mockReturnValue(of({success: false, message: 'Nope'}));

    component = build(3);

    expect(component.loadFailed).toBe(true);
    expect(component.rows).toEqual([]);
  });

  it('ignores a matrix answer for a property that is no longer selected', () => {
    const slow = new Subject<unknown>();
    chemicals.getPermissions.mockImplementation((propertyId: number) => (propertyId === 3 ? slow : of({
      success: true,
      model: [{workerId: 2, workerName: 'Ole Nielsen', flags: flags({view: true})}],
    })));
    component = build(3);

    property$.next(4);
    slow.next({success: true, model: [{workerId: 1, workerName: 'Jens Hansen', flags: flags({})}]});

    expect(component.rows.map((r) => r.workerId)).toEqual([2]);
  });

  it('ignores a save answer that arrives after the property changed', () => {
    const slowSave = new Subject<unknown>();
    chemicals.savePermissions.mockReturnValue(slowSave);
    component.toggle(component.rows[0], 'stock');
    component.save();

    property$.next(4);
    slowSave.next({success: true, model: [{workerId: 1, workerName: 'Jens Hansen', flags: flags({stock: true})}]});
    slowSave.complete();

    expect(component.rows.map((r) => r.workerId)).toEqual([2]);
    expect(component.saving).toBe(false);
  });

  it('ignores a copy answer that arrives after the property changed', () => {
    const slowCopy = new Subject<unknown>();
    chemicals.getPermissions.mockImplementation((propertyId: number) => (propertyId === 9 ? slowCopy : of({
      success: true,
      model: [{workerId: 2, workerName: 'Ole Nielsen', flags: flags({})}],
    })));
    component.copyFrom(9);

    property$.next(4);
    slowCopy.next({success: true, model: [{workerId: 2, workerName: 'Ole Nielsen', flags: flags({admin: true})}]});

    expect(component.changeCount).toBe(0);
  });
});
