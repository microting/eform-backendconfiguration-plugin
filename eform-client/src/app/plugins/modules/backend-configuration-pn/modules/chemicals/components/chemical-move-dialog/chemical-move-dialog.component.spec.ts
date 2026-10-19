import {describe, it, expect, vi} from 'vitest';
import type {Mock} from 'vitest';
import {Injector, runInInjectionContext} from '@angular/core';
import {MAT_DIALOG_DATA, MatDialogRef} from '@angular/material/dialog';
import {Subject, of} from 'rxjs';
import {ChemicalStockUnit} from '../../../../models';
import {BackendConfigurationPnChemicalsService} from '../../../../services';
import {discardConfirmHarness} from '../../helpers/chemical-dialogs.testing';
import {ChemicalMoveDialogComponent} from './chemical-move-dialog.component';

describe('ChemicalMoveDialogComponent', () => {
  let chemicals: {movePlacement: Mock};
  let dialogRef: {close: Mock};
  let confirm: ReturnType<typeof discardConfirmHarness>;

  const ALL_LOCATIONS = [{id: 10, name: 'Lade', archived: false}, {id: 11, name: 'Kemirum', archived: false}, {id: 12, name: 'Old', archived: true}];

  function build(stockEnabled: boolean, balance = 4, unit: ChemicalStockUnit | null = ChemicalStockUnit.L, locations = ALL_LOCATIONS): ChemicalMoveDialogComponent {
    chemicals = {movePlacement: vi.fn().mockReturnValue(of({success: true, model: {}}))};
    dialogRef = {close: vi.fn()};
    confirm = discardConfirmHarness();
    const injector = Injector.create({providers: [
      {provide: BackendConfigurationPnChemicalsService, useValue: chemicals},
      {provide: MatDialogRef, useValue: dialogRef},
      ...confirm.providers,
      {provide: MAT_DIALOG_DATA, useValue: {
        stockEnabled,
        row: {placement: {id: 7, locationId: 10, balance, unit, placementNote: 'Palle'}, productName: 'Hussar OD', stockUnitLabel: 'L'},
        locations,
      }},
    ]});
    return runInInjectionContext(injector, () => new ChemicalMoveDialogComponent());
  }

  it('offers active locations other than the current one', () => {
    expect(build(false).targets.map((l) => l.id)).toEqual([11]);
  });

  it('knows when there is no other active location to move to (M4)', () => {
    expect(build(false).targets.length).toBe(1);
    expect(build(false, 4, ChemicalStockUnit.L, [ALL_LOCATIONS[0], ALL_LOCATIONS[2]]).targets).toEqual([]);
  });

  it('moves everything by default', () => {
    const component = build(false);
    component.targetLocationId = 11;
    component.note = ' Reol 1 ';

    component.save();

    expect(chemicals.movePlacement).toHaveBeenCalledWith(7, {targetLocationId: 11, targetPlacementNote: 'Reol 1', amount: null});
    expect(dialogRef.close).toHaveBeenCalledWith(true);
  });

  it('a partial move (stock only) sends the amount and refuses more than the balance', () => {
    const component = build(true);
    component.targetLocationId = 11;
    component.moveAll = false;
    component.amount = 5;
    expect(component.canSave).toBe(false);

    component.amount = 1.5;
    component.save();

    expect(chemicals.movePlacement).toHaveBeenCalledWith(7, {targetLocationId: 11, targetPlacementNote: '', amount: 1.5});
  });

  it('refuses a "part" equal to the whole balance: that is "move everything" (brief: part < balance)', () => {
    const component = build(true);
    component.targetLocationId = 11;
    component.moveAll = false;

    component.amount = 4;

    expect(component.canSave).toBe(false);
  });

  it.each([1.0005, 0.0001])('refuses a partial amount of %p, which has more than three decimals (M3)', (amount) => {
    const component = build(true);
    component.targetLocationId = 11;
    component.moveAll = false;

    component.amount = amount;

    expect(component.canSave).toBe(false);
  });

  it('offers a partial move only with stock on and a counted balance', () => {
    expect(build(true).canPartial).toBe(true);
    expect(build(false).canPartial).toBe(false);
    expect(build(true, 0).canPartial).toBe(false);
    expect(build(true, 4, null).canPartial).toBe(false);
  });

  it('cannot save without a target location', () => {
    const component = build(false);

    component.save();

    expect(component.canSave).toBe(false);
    expect(chemicals.movePlacement).not.toHaveBeenCalled();
  });

  it('stays open when the server refuses the move (the host toasts its message)', () => {
    const component = build(false);
    chemicals.movePlacement.mockReturnValue(of({success: false, message: 'Ingen adgang'}));
    component.targetLocationId = 11;

    component.save();

    expect(dialogRef.close).not.toHaveBeenCalled();
    expect(component.saving).toBe(false);
  });

  it('closes with false on cancel when nothing was entered', () => {
    const component = build(false);

    component.cancel();

    expect(confirm.dialog.open).not.toHaveBeenCalled();
    expect(dialogRef.close).toHaveBeenCalledWith(false);
  });

  describe('cancel with typed input asks first (M5, confirmDiscardWhenDirty)', () => {
    it('closes only after "discard"', () => {
      const component = build(false);
      component.targetLocationId = 11;

      component.cancel();

      expect(confirm.confirmId()).toBe('chemicalMoveDiscardConfirm');
      expect(dialogRef.close).not.toHaveBeenCalled();
      confirm.discard();
      expect(dialogRef.close).toHaveBeenCalledWith(false);
    });

    it('stays open on "keep editing"', () => {
      const component = build(false);
      component.note = 'Reol 2';

      component.cancel();
      confirm.keepEditing();

      expect(dialogRef.close).not.toHaveBeenCalled();
    });

    it('does nothing while a save is in flight (M2)', () => {
      const component = build(false);
      chemicals.movePlacement.mockReturnValue(new Subject());
      component.targetLocationId = 11;
      component.save();

      component.cancel();

      expect(component.saving).toBe(true);
      expect(confirm.dialog.open).not.toHaveBeenCalled();
      expect(dialogRef.close).not.toHaveBeenCalled();
    });
  });
});
