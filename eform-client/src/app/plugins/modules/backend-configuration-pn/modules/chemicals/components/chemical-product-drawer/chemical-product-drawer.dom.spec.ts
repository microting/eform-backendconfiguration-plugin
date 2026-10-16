import {NO_ERRORS_SCHEMA} from '@angular/core';
import {ComponentFixture, TestBed} from '@angular/core/testing';
import {MAT_DIALOG_DATA, MatDialogRef} from '@angular/material/dialog';
import {TranslateModule} from '@ngx-translate/core';
import {ToastrService} from 'ngx-toastr';
import {ChemicalStockEntryKind, ChemicalStockEntryOrigin, ChemicalStockUnit} from '../../../../models';
import {BackendConfigurationPnChemicalsService} from '../../../../services';
import {buildInventoryRows} from '../../helpers';
import {entry, inventoryOf, location, placement, stockEntry} from '../../helpers/chemical-inventory.fixtures';
import {ChemicalProductDrawerComponent} from './chemical-product-drawer.component';

/** Template-level checks for W2: the e2e ids, stock hidden when stock is off, and the stock list labels (E-06). */
describe('ChemicalProductDrawerComponent (template)', () => {
  let fixture: ComponentFixture<ChemicalProductDrawerComponent>;
  const find = (selector: string): HTMLElement | null => fixture.nativeElement.querySelector(selector);

  function render(stockEnabled: boolean): void {
    const inventory = inventoryOf({
      stockEnabled,
      locations: [location(10, 'Lade')],
      placements: [placement(7, 10, 200, {balance: 3, unit: ChemicalStockUnit.Kg})],
      registerEntries: [entry(200, 'Karate 2,5 WG')],
      stockEntries: [
        stockEntry(1, 7, ChemicalStockEntryKind.Received, 5, '2026-08-12T08:00:00', 5),
        stockEntry(2, 7, ChemicalStockEntryKind.MovedOut, -2, '2026-09-02T08:00:00', 3,
          {origin: ChemicalStockEntryOrigin.Move, counterpartPlacementId: 8}),
      ],
    });
    TestBed.configureTestingModule({
      declarations: [ChemicalProductDrawerComponent],
      imports: [TranslateModule.forRoot()],
      providers: [
        {provide: BackendConfigurationPnChemicalsService, useValue: {getSdsBlob: jest.fn()}},
        {provide: MatDialogRef, useValue: {close: jest.fn()}},
        {provide: ToastrService, useValue: {error: jest.fn()}},
        {provide: MAT_DIALOG_DATA, useValue: {row: buildInventoryRows(inventory, '2026-09-29')[0], inventory, stockEnabled}},
      ],
      schemas: [NO_ERRORS_SCHEMA],
    });
    fixture = TestBed.createComponent(ChemicalProductDrawerComponent);
    fixture.detectChanges();
  }

  afterEach(() => fixture?.destroy());

  it('renders every control the e2e page object addresses', () => {
    render(true);

    for (const id of [
      'chemicalDrawer', 'chemicalDrawerTabOverview', 'chemicalDrawerTabPlacements', 'chemicalDrawerTabStock',
      'chemicalDrawerMoveBtn', 'chemicalDrawerStockBtn', 'chemicalDrawerRemoveBtn', 'chemicalDrawerSdsBtn', 'chemicalDrawerCloseBtn',
    ]) {
      expect(find(`#${id}`)).not.toBeNull();
    }
  });

  it('hides the stock tab and the stock action when stock is off', () => {
    render(false);

    expect(find('#chemicalDrawerTabStock')).toBeNull();
    expect(find('#chemicalDrawerStockBtn')).toBeNull();
    expect(find('#chemicalDrawerMoveBtn')).not.toBeNull();
  });

  it('marks only the remove action as destructive', () => {
    render(true);

    expect(fixture.nativeElement.querySelectorAll('.btn-delete')).toHaveLength(1);
    expect(find('#chemicalDrawerRemoveBtn')?.classList.contains('btn-delete')).toBe(true);
  });

  it('lists stock entries with the server balance and labels a move (E-06)', () => {
    render(true);

    find('#chemicalDrawerTabStock')?.click();
    fixture.detectChanges();

    const rows = Array.from(fixture.nativeElement.querySelectorAll('#chemicalDrawerStockHistory tbody tr')) as HTMLElement[];
    expect(rows).toHaveLength(2);
    expect(rows[0].textContent).toContain('Chemicals: Stock kind 4');
    expect(rows[0].textContent).toContain('Chemicals: Stock origin move');
    expect(rows[0].querySelector('.chemicals-stock-balance')?.textContent?.trim()).toBe('3 kg');
    expect(rows[1].textContent).not.toContain('Chemicals: Stock origin');
  });
});
