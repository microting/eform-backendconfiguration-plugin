import {describe, it, expect, afterEach, vi} from 'vitest';
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
import {detectChanges} from '../../../../testing/detect-changes';

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
      imports: [ChemicalProductDrawerComponent, TranslateModule.forRoot()],
      providers: [
        {provide: BackendConfigurationPnChemicalsService, useValue: {getSdsBlob: vi.fn()}},
        {provide: MatDialogRef, useValue: {close: vi.fn()}},
        {provide: ToastrService, useValue: {error: vi.fn()}},
        {provide: MAT_DIALOG_DATA, useValue: {row: buildInventoryRows(inventory, '2026-09-29')[0], inventory, stockEnabled}},
      ],
      schemas: [NO_ERRORS_SCHEMA],
    });
    fixture = TestBed.createComponent(ChemicalProductDrawerComponent);
    detectChanges(fixture);
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
    detectChanges(fixture);

    const rows = Array.from(fixture.nativeElement.querySelectorAll('#chemicalDrawerStockHistory tbody tr')) as HTMLElement[];
    expect(rows).toHaveLength(2);
    expect(rows[0].textContent).toContain('Chemicals: Stock kind 4');
    expect(rows[0].textContent).toContain('Chemicals: Stock origin move');
    expect(rows[0].querySelector('.chemicals-stock-balance')?.textContent?.trim()).toBe('3 kg');
    expect(rows[1].textContent).not.toContain('Chemicals: Stock origin');
  });

  it('shows the GHS diamonds in the header, the code as the text alternative (855 G1)', () => {
    render(true);

    const pictograms = Array.from(fixture.nativeElement.querySelectorAll('#chemicalDrawerPictograms img')) as HTMLImageElement[];
    expect(pictograms.map((p) => p.alt)).toEqual(['GHS07']);
  });

  it('keeps the status chip and the signal word apart in a spaced row (855 B7)', () => {
    render(true);

    const status = find('#chemicalDrawerStatus');
    expect(status?.classList.contains('chemicals-drawer__status')).toBe(true);
    expect(status?.querySelector('.status-chip')).not.toBeNull();
    expect(find('#chemicalDrawerSignalWord')?.parentElement).toBe(status);
  });

  describe('tabs follow the WAI-ARIA tabs pattern (M8)', () => {
    const tabIds = ['chemicalDrawerTabOverview', 'chemicalDrawerTabPlacements', 'chemicalDrawerTabStock'];

    it('has a tablist of tabs, each controlling its labelled tabpanel, with one tab in the tab order', () => {
      render(true);

      expect(find('[role="tablist"]')?.getAttribute('aria-label')).toBeTruthy();
      const tabs = tabIds.map((id) => find(`#${id}`) as HTMLElement);
      expect(tabs.map((t) => t.getAttribute('role'))).toEqual(['tab', 'tab', 'tab']);
      expect(tabs.map((t) => t.getAttribute('aria-selected'))).toEqual(['true', 'false', 'false']);
      expect(tabs.map((t) => t.getAttribute('tabindex'))).toEqual(['0', '-1', '-1']);
      const panel = find('[role="tabpanel"]') as HTMLElement;
      expect(tabs[0].getAttribute('aria-controls')).toBe(panel.id);
      expect(panel.getAttribute('aria-labelledby')).toBe('chemicalDrawerTabOverview');
    });

    it('selects and focuses the next tab on ArrowRight', () => {
      render(true);

      find('#chemicalDrawerTabOverview')?.dispatchEvent(new KeyboardEvent('keydown', {key: 'ArrowRight', bubbles: true}));
      detectChanges(fixture);

      const placements = find('#chemicalDrawerTabPlacements') as HTMLElement;
      expect(placements.getAttribute('aria-selected')).toBe('true');
      expect(placements.getAttribute('tabindex')).toBe('0');
      expect(document.activeElement).toBe(placements);
      expect(find('[role="tabpanel"]')?.getAttribute('aria-labelledby')).toBe('chemicalDrawerTabPlacements');
    });

    it('is not a navigation landmark', () => {
      render(true);

      expect(find('nav[role="tablist"]')).toBeNull();
    });
  });
});
