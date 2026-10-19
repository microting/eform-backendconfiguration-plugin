import {Injector, runInInjectionContext} from '@angular/core';
import {MAT_DIALOG_DATA, MatDialogRef} from '@angular/material/dialog';
import {TranslateService} from '@ngx-translate/core';
import {ToastrService} from 'ngx-toastr';
import {of, throwError} from 'rxjs';
import {ChemicalStockEntryKind} from '../../../../models';
import {BackendConfigurationPnChemicalsService} from '../../../../services';
import {inventoryOf, location, placement, stockEntry} from '../../helpers/chemical-inventory.fixtures';
import {ChemicalProductDrawerComponent} from './chemical-product-drawer.component';

describe('ChemicalProductDrawerComponent', () => {
  function build(stockEnabled: boolean, getSdsBlob = jest.fn().mockReturnValue(of(new Blob()))) {
    const dialogRef = {close: jest.fn()};
    const toastr = {error: jest.fn()};
    const row = {placement: {id: 7, chemicalId: 100, productId: null}, entry: {products: [{productId: 1, sdsFileName: 'abc'}]}, productName: 'Karate'};
    const inventory = inventoryOf({
      locations: [location(10, 'Lade'), location(11, 'Kemirum')],
      placements: [placement(7, 10, 100), placement(8, 11, 100), placement(9, 11, 100, {removedAt: '2026-09-01T00:00:00'}), placement(10, 11, 200)],
      stockEntries: [
        stockEntry(1, 7, ChemicalStockEntryKind.Received, 5, '2026-08-12T08:00:00', 5),
        stockEntry(2, 7, ChemicalStockEntryKind.Consumed, -1, '2026-09-02T08:00:00', 4),
        stockEntry(3, 8, ChemicalStockEntryKind.Received, 2, '2026-09-02T08:00:00', 2),
      ],
    });
    const injector = Injector.create({providers: [
      {provide: BackendConfigurationPnChemicalsService, useValue: {getSdsBlob}},
      {provide: MatDialogRef, useValue: dialogRef},
      {provide: ToastrService, useValue: toastr},
      {provide: TranslateService, useValue: {instant: (k: string) => k}},
      {provide: MAT_DIALOG_DATA, useValue: {row, stockEnabled, inventory}},
    ]});
    return {component: runInInjectionContext(injector, () => new ChemicalProductDrawerComponent()), dialogRef, row, toastr};
  }

  it('hands the chosen action back to the page', () => {
    const {component, dialogRef, row} = build(true);

    component.act('move');

    expect(dialogRef.close).toHaveBeenCalledWith({action: 'move', row});
  });

  it('closes with no action from the close button', () => {
    const {component, dialogRef} = build(true);

    component.close();

    expect(dialogRef.close).toHaveBeenCalledWith(undefined);
  });

  it('lists the stock tab only when stock is enabled', () => {
    expect(build(true).component.tabs.map((t) => t.id)).toEqual(['overview', 'placements', 'stock']);
    expect(build(false).component.tabs.map((t) => t.id)).toEqual(['overview', 'placements']);
  });

  it('moves between the tabs with the arrow keys, Home and End, wrapping around (WAI-ARIA tabs)', () => {
    const {component} = build(true);
    const key = (k: string) => component.onTabKeydown({key: k, preventDefault: jest.fn()} as unknown as KeyboardEvent);

    key('ArrowRight');
    expect(component.tab).toBe('placements');
    key('End');
    expect(component.tab).toBe('stock');
    key('ArrowRight');
    expect(component.tab).toBe('overview');
    key('ArrowLeft');
    expect(component.tab).toBe('stock');
    key('Home');
    expect(component.tab).toBe('overview');
    key('a');
    expect(component.tab).toBe('overview');
  });

  it('never lands on the stock tab with the keyboard when stock is off', () => {
    const {component} = build(false);

    component.onTabKeydown({key: 'ArrowLeft', preventDefault: jest.fn()} as unknown as KeyboardEvent);

    expect(component.tab).toBe('placements');
  });

  it('lists the chemical\'s open placements on the property and this placement\'s stock history', () => {
    const {component} = build(true);

    expect(component.placements.map((p) => p.id)).toEqual([7, 8]);
    expect(component.locationName(11)).toBe('Kemirum');
    expect(component.stockHistory.map((h) => [h.entry.id, h.balanceAfter])).toEqual([[2, 4], [1, 5]]);
  });

  it('opens the safety data sheet and toasts when it cannot be fetched', () => {
    const failing = jest.fn().mockReturnValue(throwError(() => new Error('404')));
    const {component, toastr} = build(true, failing);

    component.openSds();

    expect(failing).toHaveBeenCalledWith('abc');
    expect(toastr.error).toHaveBeenCalledWith('Chemicals: Could not open safety data sheet');
  });
});
