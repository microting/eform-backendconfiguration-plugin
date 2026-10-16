import {Injector, runInInjectionContext} from '@angular/core';
import {MAT_DIALOG_DATA, MatDialogRef} from '@angular/material/dialog';
import {of} from 'rxjs';
import {ChemicalStockUnit} from '../../../../models';
import {BackendConfigurationPnChemicalsService} from '../../../../services';
import {ChemicalMoveDialogComponent} from './chemical-move-dialog.component';

describe('ChemicalMoveDialogComponent', () => {
  let chemicals: {movePlacement: jest.Mock};
  let dialogRef: {close: jest.Mock};

  function build(stockEnabled: boolean, balance = 4, unit: ChemicalStockUnit | null = ChemicalStockUnit.L): ChemicalMoveDialogComponent {
    chemicals = {movePlacement: jest.fn().mockReturnValue(of({success: true, model: {}}))};
    dialogRef = {close: jest.fn()};
    const injector = Injector.create({providers: [
      {provide: BackendConfigurationPnChemicalsService, useValue: chemicals},
      {provide: MatDialogRef, useValue: dialogRef},
      {provide: MAT_DIALOG_DATA, useValue: {
        stockEnabled,
        row: {placement: {id: 7, locationId: 10, balance, unit, placementNote: 'Palle'}, productName: 'Hussar OD', stockUnitLabel: 'L'},
        locations: [{id: 10, name: 'Lade', archived: false}, {id: 11, name: 'Kemirum', archived: false}, {id: 12, name: 'Old', archived: true}],
      }},
    ]});
    return runInInjectionContext(injector, () => new ChemicalMoveDialogComponent());
  }

  it('offers active locations other than the current one', () => {
    expect(build(false).targets.map((l) => l.id)).toEqual([11]);
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

  it('closes with false on cancel', () => {
    const component = build(false);

    component.cancel();

    expect(dialogRef.close).toHaveBeenCalledWith(false);
  });
});
