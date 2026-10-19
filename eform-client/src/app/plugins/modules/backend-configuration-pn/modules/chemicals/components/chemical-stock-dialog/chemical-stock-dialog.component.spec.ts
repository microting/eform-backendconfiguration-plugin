import {describe, it, expect, vi} from 'vitest';
import type {Mock} from 'vitest';
import {Injector, runInInjectionContext} from '@angular/core';
import {MAT_DIALOG_DATA, MatDialogRef} from '@angular/material/dialog';
import {Subject, of} from 'rxjs';
import {ChemicalStockEntryKind, ChemicalStockUnit} from '../../../../models';
import {BackendConfigurationPnChemicalsService} from '../../../../services';
import {discardConfirmHarness} from '../../helpers/chemical-dialogs.testing';
import {ChemicalStockDialogComponent} from './chemical-stock-dialog.component';

describe('ChemicalStockDialogComponent', () => {
  let chemicals: {addStockEntry: Mock};
  let dialogRef: {close: Mock};
  let confirm: ReturnType<typeof discardConfirmHarness>;

  function build(unit: ChemicalStockUnit | null): ChemicalStockDialogComponent {
    chemicals = {addStockEntry: vi.fn().mockReturnValue(of({success: true, model: {}}))};
    dialogRef = {close: vi.fn()};
    confirm = discardConfirmHarness();
    const injector = Injector.create({providers: [
      {provide: BackendConfigurationPnChemicalsService, useValue: chemicals},
      {provide: MatDialogRef, useValue: dialogRef},
      ...confirm.providers,
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

  it('accepts consuming exactly the whole balance (server: consumed <= balance)', () => {
    const component = build(ChemicalStockUnit.Kg);
    component.kind = ChemicalStockEntryKind.Consumed;

    component.amount = 3.5;

    expect(component.canSave).toBe(true);
  });

  it('refuses receiving nothing (server: the amount must be greater than zero)', () => {
    const component = build(ChemicalStockUnit.Kg);
    component.kind = ChemicalStockEntryKind.Received;

    component.amount = 0;

    expect(component.canSave).toBe(false);
  });

  it('refuses an adjustment to the current balance, which the server rejects as no change (E-20)', () => {
    const component = build(ChemicalStockUnit.Kg);
    component.kind = ChemicalStockEntryKind.Adjusted;

    component.amount = 3.5;

    expect(component.canSave).toBe(false);
  });

  it.each([
    [ChemicalStockEntryKind.Received, 1.0005],
    [ChemicalStockEntryKind.Received, 1_000_000.5],
    [ChemicalStockEntryKind.Received, 2_000_000],
    [ChemicalStockEntryKind.Adjusted, 0.0004],
    [ChemicalStockEntryKind.Adjusted, 1_000_001],
  ])('refuses kind %p with amount %p: more than three decimals or above one million (M3)', (kind, amount) => {
    const component = build(ChemicalStockUnit.Kg);
    component.kind = kind;

    component.amount = amount;

    expect(component.canSave).toBe(false);
  });

  it('accepts the server maximum of one million', () => {
    const component = build(ChemicalStockUnit.Kg);
    component.kind = ChemicalStockEntryKind.Received;

    component.amount = 1_000_000;

    expect(component.canSave).toBe(true);
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

  it('closes with false on cancel when nothing was entered', () => {
    const component = build(null);

    component.cancel();

    expect(confirm.dialog.open).not.toHaveBeenCalled();
    expect(dialogRef.close).toHaveBeenCalledWith(false);
  });

  describe('cancel with typed input asks first (M5)', () => {
    it.each([
      ['an amount', (c: ChemicalStockDialogComponent) => (c.amount = 2)],
      ['a batch', (c: ChemicalStockDialogComponent) => (c.batchLot = 'L-1')],
      ['a note', (c: ChemicalStockDialogComponent) => (c.note = 'x')],
    ])('asks after %s and closes only on "discard"', (_name, edit) => {
      const component = build(ChemicalStockUnit.Kg);
      edit(component);

      component.cancel();

      expect(confirm.confirmId()).toBe('chemicalStockDiscardConfirm');
      expect(dialogRef.close).not.toHaveBeenCalled();
      confirm.discard();
      expect(dialogRef.close).toHaveBeenCalledWith(false);
    });

    it('does nothing while the entry is being saved (M2)', () => {
      const component = build(ChemicalStockUnit.Kg);
      chemicals.addStockEntry.mockReturnValue(new Subject());
      component.kind = ChemicalStockEntryKind.Received;
      component.amount = 1;
      component.save();

      component.cancel();

      expect(confirm.dialog.open).not.toHaveBeenCalled();
      expect(dialogRef.close).not.toHaveBeenCalled();
    });
  });
});
