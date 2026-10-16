import {Injector, runInInjectionContext} from '@angular/core';
import {MAT_DIALOG_DATA, MatDialogRef} from '@angular/material/dialog';
import {of} from 'rxjs';
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
});
