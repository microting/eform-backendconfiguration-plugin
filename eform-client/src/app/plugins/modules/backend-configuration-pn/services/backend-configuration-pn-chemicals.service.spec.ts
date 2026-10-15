import {Injector, runInInjectionContext} from '@angular/core';
import {Observable, of} from 'rxjs';
import {ApiBaseService} from 'src/app/common/services';
import {
  BackendConfigurationPnChemicalsMethods,
  BackendConfigurationPnChemicalsService,
} from './backend-configuration-pn-chemicals.service';
import {
  CHEMICAL_STATUS_APPROVED,
  CHEMICAL_STATUS_DEREGISTERED,
  CHEMICAL_STATUS_EXPIRED,
  ChemicalAlertThreshold,
  ChemicalDeadlineKind,
  ChemicalRemovalReason,
  ChemicalStockEntryKind,
  ChemicalStockEntryOrigin,
  ChemicalStockUnit,
} from '../models';

/**
 * Pins every route, verb and body of the Kemi web service against ChemicalsController
 * (Plan A Task 15) and the enum integers against the C# enums.
 */
describe('BackendConfigurationPnChemicalsService', () => {
  const BASE = BackendConfigurationPnChemicalsMethods.Base;
  let api: Record<string, jest.Mock>;
  let service: BackendConfigurationPnChemicalsService;

  beforeEach(() => {
    api = {
      get: jest.fn().mockReturnValue(of({success: true, model: {}})),
      post: jest.fn().mockReturnValue(of({success: true, model: {}})),
      put: jest.fn().mockReturnValue(of({success: true, model: {}})),
      postFormData: jest.fn().mockReturnValue(of({success: true, model: {}})),
      getBlobData: jest.fn().mockReturnValue(of(new Blob([]))),
    };
    const injector = Injector.create({providers: [{provide: ApiBaseService, useValue: api}]});
    service = runInInjectionContext(injector, () => new BackendConfigurationPnChemicalsService());
  });

  const amount = {containerSize: null, containerCount: null, amount: 2, unit: ChemicalStockUnit.L, batchLot: null, note: null, at: null};
  const file = new File(['x'], 'lade.jpg', {type: 'image/jpeg'});

  const cases: Array<[string, (s: BackendConfigurationPnChemicalsService) => Observable<unknown>, string, unknown[]]> = [
    ['inventory', (s) => s.getPropertyInventory(3), 'get', [`${BASE}/properties/3/inventory`]],
    ['register search', (s) => s.searchRegister('round', 1, 10), 'get', [`${BASE}/register/search`, {query: 'round', page: 1, pageSize: 10}]],
    ['register search defaults', (s) => s.searchRegister('x'), 'get', [`${BASE}/register/search`, {query: 'x', page: 0, pageSize: 25}]],
    ['barcode lookup encodes', (s) => s.lookupBarcode('57 01'), 'get', [`${BASE}/register/barcode/57%2001`]],
    ['sds blob encodes', (s) => s.getSdsBlob('a b.pdf'), 'getBlobData', [`${BASE}/sds/a%20b.pdf`]],
    ['register placement', (s) => s.registerPlacement({locationId: 1, chemicalId: 2, productId: null, placementNote: 'Hylde', initialStock: null}), 'post',
      [`${BASE}/placements`, {locationId: 1, chemicalId: 2, productId: null, placementNote: 'Hylde', initialStock: null}]],
    ['move', (s) => s.movePlacement(7, {targetLocationId: 9, targetPlacementNote: 'Reol', amount: null}), 'post',
      [`${BASE}/placements/7/move`, {targetLocationId: 9, targetPlacementNote: 'Reol', amount: null}]],
    ['remove', (s) => s.removePlacement(7, {reason: ChemicalRemovalReason.Used, removedAt: null, note: ''}), 'post',
      [`${BASE}/placements/7/remove`, {reason: ChemicalRemovalReason.Used, removedAt: null, note: ''}]],
    ['note', (s) => s.updatePlacementNote(7, {placementNote: 'Skab'}), 'put', [`${BASE}/placements/7/note`, {placementNote: 'Skab'}]],
    ['stock entry', (s) => s.addStockEntry(7, {kind: ChemicalStockEntryKind.Consumed, amount}), 'post',
      [`${BASE}/placements/7/stock-entries`, {kind: ChemicalStockEntryKind.Consumed, amount}]],
    ['create location', (s) => s.createLocation({propertyId: 3, name: 'Lade', description: '', sortOrder: null}), 'post',
      [`${BASE}/locations`, {propertyId: 3, name: 'Lade', description: '', sortOrder: null}]],
    ['update location', (s) => s.updateLocation(4, {name: 'Lade 2', description: 'x', sortOrder: null}), 'put',
      [`${BASE}/locations/4`, {name: 'Lade 2', description: 'x', sortOrder: null}]],
    ['archive location', (s) => s.archiveLocation(4), 'post', [`${BASE}/locations/4/archive`, {}]],
    ['reorder locations', (s) => s.reorderLocations(3, [5, 4]), 'put', [`${BASE}/properties/3/locations/order`, [5, 4]]],
    ['upload photo', (s) => s.uploadLocationPhoto(4, file), 'postFormData', [`${BASE}/locations/4/photo`, {file}]],
    ['permissions', (s) => s.getPermissions(3), 'get', [`${BASE}/properties/3/permissions`]],
    ['save permissions', (s) => s.savePermissions(3, []), 'put', [`${BASE}/properties/3/permissions`, []]],
    ['settings', (s) => s.getSettings(3), 'get', [`${BASE}/properties/3/settings`]],
    ['save settings', (s) => s.saveSettings(3, {stockEnabled: true, digestRecipients: ['first@example.com']}), 'put',
      [`${BASE}/properties/3/settings`, {stockEnabled: true, digestRecipients: ['first@example.com']}]],
  ];

  it.each(cases)('%s calls the Plan A route', (_name, call, verb, args) => {
    call(service).subscribe();

    expect(api[verb]).toHaveBeenCalledTimes(1);
    expect(api[verb]).toHaveBeenCalledWith(...args);
  });

  it('builds the location photo URL for the authImage pipe', () => {
    expect(service.locationPhotoUrl(4)).toBe(`/${BASE}/locations/4/photo`);
  });

  it('keeps the published prefix', () => {
    expect(BASE).toBe('api/backend-configuration-pn/chemicals');
  });

  it('pins the enum integers to the C# enums', () => {
    expect([ChemicalRemovalReason.Used, ChemicalRemovalReason.Disposed, ChemicalRemovalReason.Moved]).toEqual([1, 2, 3]);
    expect([
      ChemicalStockEntryKind.Received, ChemicalStockEntryKind.Consumed, ChemicalStockEntryKind.Adjusted,
      ChemicalStockEntryKind.MovedOut, ChemicalStockEntryKind.MovedIn,
    ]).toEqual([1, 2, 3, 4, 5]);
    expect([ChemicalStockEntryOrigin.Manual, ChemicalStockEntryOrigin.RemovalWriteOff, ChemicalStockEntryOrigin.Move]).toEqual([1, 2, 3]);
    expect([ChemicalStockUnit.L, ChemicalStockUnit.Kg]).toEqual([1, 2]);
    expect([
      ChemicalAlertThreshold.SixMonths, ChemicalAlertThreshold.OneMonth,
      ChemicalAlertThreshold.Passed, ChemicalAlertThreshold.StatusChanged,
    ]).toEqual([1, 2, 3, 4]);
    expect([ChemicalDeadlineKind.UseAndPossession, ChemicalDeadlineKind.Sales]).toEqual([1, 2]);
    expect([CHEMICAL_STATUS_APPROVED, CHEMICAL_STATUS_DEREGISTERED, CHEMICAL_STATUS_EXPIRED]).toEqual([5, 6, 7]);
  });
});
