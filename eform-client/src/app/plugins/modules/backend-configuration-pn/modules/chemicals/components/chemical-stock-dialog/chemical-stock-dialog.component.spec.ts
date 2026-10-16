import {Injector, runInInjectionContext} from '@angular/core';
import {MAT_DIALOG_DATA, MatDialogRef} from '@angular/material/dialog';
import {of} from 'rxjs';
import {ChemicalStockEntryKind, ChemicalStockUnit} from '../../../../models';
import {BackendConfigurationPnChemicalsService} from '../../../../services';
import {ChemicalStockDialogComponent} from './chemical-stock-dialog.component';

describe('ChemicalStockDialogComponent', () => {
  let chemicals: {addStockEntry: jest.Mock};
  let dialogRef: {close: jest.Mock};

  function build(unit: ChemicalStockUnit | null): ChemicalStockDialogComponent {
    chemicals = {addStockEntry: jest.fn().mockReturnValue(of({success: true, model: {}}))};
    dialogRef = {close: jest.fn()};
    const injector = Injector.create({providers: [
      {provide: BackendConfigurationPnChemicalsService, useValue: chemicals},
      {provide: MatDialogRef, useValue: dialogRef},
      {provide: MAT_DIALOG_DATA, useValue: {row: {placement: {id: 7, balance: 3.5, unit}, productName: 'Karate'}}},
    ]});
    return runInInjectionContext(injector, () => new ChemicalStockDialogComponent());
  }

  it('locks the unit to the placement\'s existing unit', () => {
    const component = build(ChemicalStockUnit.Kg);

    expect(component.unit).toBe(ChemicalStockUnit.Kg);
    expect(component.unitLocked).toBe(true);
    expect(build(null).unitLocked).toBe(false);
  });

  it('books a consumption as a positive amount of the chosen kind', () => {
    const component = build(ChemicalStockUnit.Kg);
    component.kind = ChemicalStockEntryKind.Consumed;
    component.amount = 0.5;
    component.note = ' sprøjtning ';

    component.save();

    expect(chemicals.addStockEntry).toHaveBeenCalledWith(7, {
      kind: ChemicalStockEntryKind.Consumed,
      amount: {containerSize: null, containerCount: null, amount: 0.5, unit: ChemicalStockUnit.Kg, batchLot: null, note: 'sprøjtning', at: null},
    });
    expect(dialogRef.close).toHaveBeenCalledWith(true);
  });

  it('refuses zero, negative and more-than-balance consumption; adjust accepts zero (counted empty)', () => {
    const component = build(ChemicalStockUnit.Kg);
    component.kind = ChemicalStockEntryKind.Consumed;
    for (const amount of [0, -1, 4]) {
      component.amount = amount;
      expect(component.canSave).toBe(false);
    }
    component.kind = ChemicalStockEntryKind.Adjusted;
    component.amount = 0;
    expect(component.canSave).toBe(true);
  });

  it('refuses an adjustment to the current balance, which the server rejects as no change (E-20)', () => {
    const component = build(ChemicalStockUnit.Kg);
    component.kind = ChemicalStockEntryKind.Adjusted;

    component.amount = 3.5;

    expect(component.canSave).toBe(false);
  });

  it('sends the batch only for received goods', () => {
    const component = build(null);
    component.kind = ChemicalStockEntryKind.Received;
    component.amount = 10;
    component.batchLot = ' L-42 ';

    component.save();

    expect(chemicals.addStockEntry.mock.lastCall[1].amount.batchLot).toBe('L-42');

    component.kind = ChemicalStockEntryKind.Consumed;
    component.amount = 1;
    component.save();

    expect(chemicals.addStockEntry.mock.lastCall[1].amount.batchLot).toBeNull();
  });

  it('stays open when the server refuses the entry (the host toasts its message)', () => {
    const component = build(ChemicalStockUnit.Kg);
    chemicals.addStockEntry.mockReturnValue(of({success: false, message: 'Ingen adgang'}));
    component.kind = ChemicalStockEntryKind.Received;
    component.amount = 1;

    component.save();

    expect(dialogRef.close).not.toHaveBeenCalled();
    expect(component.saving).toBe(false);
  });

  it('closes with false on cancel', () => {
    const component = build(null);

    component.cancel();

    expect(dialogRef.close).toHaveBeenCalledWith(false);
  });
});
