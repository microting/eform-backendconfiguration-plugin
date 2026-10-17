import {Injector, runInInjectionContext} from '@angular/core';
import {MAT_DIALOG_DATA, MatDialogRef} from '@angular/material/dialog';
import {Subject, of} from 'rxjs';
import {ChemicalStockUnit} from '../../../../models';
import {BackendConfigurationPnChemicalsService} from '../../../../services';
import {ChemicalRegisterPlacementDialogComponent} from './chemical-register-placement-dialog.component';

describe('ChemicalRegisterPlacementDialogComponent', () => {
  let chemicals: {searchRegister: jest.Mock; registerPlacement: jest.Mock};
  let dialogRef: {close: jest.Mock};

  function build(stockEnabled: boolean): ChemicalRegisterPlacementDialogComponent {
    chemicals = {
      searchRegister: jest.fn().mockReturnValue(of({success: true, model: {total: 1, entries: [
        {chemicalId: 200, name: 'Karate 2,5 WG', registrationNo: '1-212', products: [{productId: 2000, name: 'Karate 1 L'}]},
      ]}})),
      registerPlacement: jest.fn().mockReturnValue(of({success: true, model: {placements: [], stockEntries: [], registerEntries: []}})),
    };
    dialogRef = {close: jest.fn()};
    const injector = Injector.create({providers: [
      {provide: BackendConfigurationPnChemicalsService, useValue: chemicals},
      {provide: MatDialogRef, useValue: dialogRef},
      {provide: MAT_DIALOG_DATA, useValue: {
        propertyId: 3, stockEnabled,
        locations: [{id: 10, name: 'Lade', archived: false}, {id: 11, name: 'Gammel', archived: true}],
      }},
    ]});
    return runInInjectionContext(injector, () => new ChemicalRegisterPlacementDialogComponent());
  }

  it('offers only active locations', () => {
    expect(build(false).locations.map((l) => l.id)).toEqual([10]);
  });

  it('searches the register with the trimmed query', () => {
    const component = build(false);
    component.query = '  karate ';

    component.search();

    expect(chemicals.searchRegister).toHaveBeenCalledWith('karate', 0, 25);
    expect(component.results.map((r) => r.chemicalId)).toEqual([200]);
  });

  it('cannot save before a product and a location are chosen', () => {
    const component = build(false);

    expect(component.canSave).toBe(false);
    component.save();
    expect(chemicals.registerPlacement).not.toHaveBeenCalled();
  });

  it('registers without stock when stock is off, and closes with true', () => {
    const component = build(false);
    component.query = 'karate';
    component.search();
    component.select(component.results[0]);
    component.locationId = 10;
    component.placementNote = ' Aflåst skab ';

    component.save();

    expect(chemicals.registerPlacement).toHaveBeenCalledWith({
      locationId: 10, chemicalId: 200, productId: null, placementNote: 'Aflåst skab', initialStock: null,
    });
    expect(dialogRef.close).toHaveBeenCalledWith(true);
  });

  it('sends the initial stock when stock is on and an amount is given', () => {
    const component = build(true);
    component.query = 'karate';
    component.search();
    component.select(component.results[0]);
    component.productId = 2000;
    component.locationId = 10;
    component.amount = 5;
    component.unit = ChemicalStockUnit.Kg;

    component.save();

    expect(chemicals.registerPlacement).toHaveBeenCalledWith(expect.objectContaining({
      productId: 2000,
      initialStock: {containerSize: null, containerCount: null, amount: 5, unit: ChemicalStockUnit.Kg, batchLot: null, note: null, at: null},
    }));
  });
  it('ignores a blank query', () => {
    const component = build(false);
    component.query = '   ';

    component.search();

    expect(chemicals.searchRegister).not.toHaveBeenCalled();
  });

  it('stays open when the server refuses the placement (the host toasts its message)', () => {
    const component = build(false);
    chemicals.registerPlacement.mockReturnValue(of({success: false, message: 'Ingen adgang'}));
    component.query = 'karate';
    component.search();
    component.select(component.results[0]);
    component.locationId = 10;

    component.save();

    expect(dialogRef.close).not.toHaveBeenCalled();
    expect(component.saving).toBe(false);
  });

  it('closes with false on cancel', () => {
    const component = build(false);

    component.cancel();

    expect(dialogRef.close).toHaveBeenCalledWith(false);
  });

  describe('a new search (I-2)', () => {
    const page = (entries: Array<{chemicalId: number; name: string}>, total = entries.length) =>
      ({success: true, model: {total, entries: entries.map((e) => ({...e, registrationNo: '', products: []}))}});

    it('drops the earlier pick, so Save cannot register a chemical the user no longer sees', () => {
      const component = build(false);
      component.query = 'karate';
      component.search();
      component.select(component.results[0]);
      component.locationId = 10;
      expect(component.canSave).toBe(true);

      chemicals.searchRegister.mockReturnValue(of(page([{chemicalId: 300, name: 'Virkon S'}])));
      component.query = 'virkon';
      component.search();

      expect(component.selected).toBeNull();
      expect(component.productId).toBeNull();
      expect(component.canSave).toBe(false);
    });

    it('drops a pick made from the old results while the new search was pending (N-m1)', () => {
      const component = build(false);
      component.query = 'karate';
      component.search();
      component.locationId = 10;
      const pending = new Subject<unknown>();
      chemicals.searchRegister.mockReturnValue(pending);
      component.query = 'virkon';
      component.search();

      component.select(component.results[0]);
      pending.next(page([{chemicalId: 300, name: 'Virkon S'}]));

      expect(component.selected).toBeNull();
      expect(component.productId).toBeNull();
      expect(component.canSave).toBe(false);
    });

    it('ignores an older answer that arrives after a newer one', () => {
      const component = build(false);
      const slow = new Subject<unknown>();
      chemicals.searchRegister.mockReturnValueOnce(slow).mockReturnValueOnce(of(page([{chemicalId: 300, name: 'Virkon S'}])));
      component.query = 'karate';
      component.search();
      component.query = 'virkon';
      component.search();

      slow.next(page([{chemicalId: 200, name: 'Karate 2,5 WG'}]));

      expect(component.results.map((r) => r.chemicalId)).toEqual([300]);
    });

    it('tells the user when the register has more matches than it shows (m-4)', () => {
      const component = build(false);
      chemicals.searchRegister.mockReturnValue(of(page([{chemicalId: 300, name: 'Virkon S'}], 40)));
      component.query = 'vir';

      component.search();

      expect(component.total).toBe(40);
      expect(component.hasMore).toBe(true);
    });
  });

  it('needs at least two characters before it asks the register (m-4)', () => {
    const component = build(false);
    component.query = ' k ';

    component.search();

    expect(chemicals.searchRegister).not.toHaveBeenCalled();
  });

  it.each([-5, 0])('cannot save a received amount of %p (m-4)', (amount) => {
    const component = build(true);
    component.query = 'karate';
    component.search();
    component.select(component.results[0]);
    component.locationId = 10;

    component.amount = amount;

    expect(component.canSave).toBe(false);
    component.amount = null;
    expect(component.canSave).toBe(true);
  });

  it.each([1.0005, 1_000_001])('cannot save an initial amount of %p: more than three decimals or above one million (M3)', (amount) => {
    const component = build(true);
    component.query = 'karate';
    component.search();
    component.select(component.results[0]);
    component.locationId = 10;

    component.amount = amount;

    expect(component.canSave).toBe(false);
  });

  it('ignores cancel while the registration is in flight (M2)', () => {
    const component = build(false);
    chemicals.registerPlacement.mockReturnValue(new Subject());
    component.query = 'karate';
    component.search();
    component.select(component.results[0]);
    component.locationId = 10;
    component.save();

    component.cancel();

    expect(dialogRef.close).not.toHaveBeenCalled();
  });

  it('picks the package whose barcode was searched (m-5)', () => {
    const component = build(false);
    chemicals.searchRegister.mockReturnValue(of({success: true, model: {total: 1, entries: [{
      chemicalId: 200, name: 'Karate 2,5 WG', registrationNo: '1-212', products: [
        {productId: 2000, name: 'Karate 1 L', barcode: '5701234000017'},
        {productId: 2001, name: 'Karate 5 L', barcode: '5701234000024'},
      ],
    }]}}));
    component.query = ' 05701234000024 ';
    component.search();

    component.select(component.results[0]);

    expect(component.productId).toBe(2001);
  });
});
