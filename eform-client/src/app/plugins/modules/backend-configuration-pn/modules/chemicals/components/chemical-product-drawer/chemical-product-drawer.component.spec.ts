import {describe, it, expect, vi, beforeEach, afterEach} from 'vitest';
import {Injector, runInInjectionContext} from '@angular/core';
import {MAT_DIALOG_DATA, MatDialogRef} from '@angular/material/dialog';
import {TranslateService} from '@ngx-translate/core';
import {ToastrService} from 'ngx-toastr';
import {of, throwError} from 'rxjs';
import {ChemicalLabelKind, ChemicalStockEntryKind} from '../../../../models';
import {BackendConfigurationPnChemicalsService} from '../../../../services';
import {inventoryOf, labelOf, location, placement, sdsDocument, stockEntry} from '../../helpers/chemical-inventory.fixtures';
import {ChemicalProductDrawerComponent} from './chemical-product-drawer.component';

describe('ChemicalProductDrawerComponent', () => {
  function build(stockEnabled: boolean, getSdsBlob = vi.fn().mockReturnValue(of(new Blob())), extra: {entry?: object; lang?: string} = {}) {
    const getLabelBlob = vi.fn().mockReturnValue(of(new Blob()));
    const dialogRef = {close: vi.fn()};
    const toastr = {error: vi.fn(), info: vi.fn()};
    const row = {placement: {id: 7, chemicalId: 100, productId: null}, entry: {products: [{productId: 1, sdsFileName: 'abc'}], ...extra.entry}, productName: 'Karate'};
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
      {provide: BackendConfigurationPnChemicalsService, useValue: {getSdsBlob, getLabelBlob}},
      {provide: MatDialogRef, useValue: dialogRef},
      {provide: ToastrService, useValue: toastr},
      {provide: TranslateService, useValue: {instant: (k: string) => k, getCurrentLang: () => extra.lang ?? 'da', getFallbackLang: () => 'da'}},
      {provide: MAT_DIALOG_DATA, useValue: {row, stockEnabled, inventory}},
    ]});
    return {component: runInInjectionContext(injector, () => new ChemicalProductDrawerComponent()), dialogRef, row, toastr, getLabelBlob};
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
    const key = (k: string) => component.onTabKeydown({key: k, preventDefault: vi.fn()} as unknown as KeyboardEvent);

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

    component.onTabKeydown({key: 'ArrowLeft', preventDefault: vi.fn()} as unknown as KeyboardEvent);

    expect(component.tab).toBe('placements');
  });

  it('lists the chemical\'s open placements on the property and this placement\'s stock history', () => {
    const {component} = build(true);

    expect(component.placements.map((p) => p.id)).toEqual([7, 8]);
    expect(component.locationName(11)).toBe('Kemirum');
    expect(component.stockHistory.map((h) => [h.entry.id, h.balanceAfter])).toEqual([[2, 4], [1, 5]]);
  });

  it('opens the safety data sheet and toasts when it cannot be fetched', () => {
    const failing = vi.fn().mockReturnValue(throwError(() => new Error('404')));
    const {component, toastr} = build(true, failing);

    component.openSds();

    expect(failing).toHaveBeenCalledWith('abc');
    expect(toastr.error).toHaveBeenCalledWith('Chemicals: Could not open safety data sheet');
  });

  describe('SDS in the user language and labels (Task 15, 15b)', () => {
    // The downloads open a tab first; a stub keeps the spec off the real window.
    beforeEach(() => {
      vi.spyOn(window, 'open').mockReturnValue({location: {href: ''}, close: vi.fn()} as unknown as Window);
    });
    afterEach(() => vi.restoreAllMocks());

    it('opens the SDS in the language of the eForm user', () => {
      const getSds = vi.fn().mockReturnValue(of(new Blob()));
      const {component} = build(true, getSds, {lang: 'en-US', entry: {sdsDocuments: [sdsDocument('da'), sdsDocument('en')]}});

      component.openSds();

      expect(getSds).toHaveBeenCalledWith('md5en');
      expect(component.sdsFileName).toBe('md5en');
    });

    it('falls back to the product sheet for old data without SDS documents', () => {
      const getSds = vi.fn().mockReturnValue(of(new Blob()));
      const {component} = build(true, getSds, {lang: 'en-US'});

      component.openSds();

      expect(getSds).toHaveBeenCalledWith('abc');
    });

    it('lists the other languages and opens the chosen one', () => {
      const getSds = vi.fn().mockReturnValue(of(new Blob()));
      const {component} = build(true, getSds, {lang: 'en-US', entry: {sdsDocuments: [sdsDocument('sv'), sdsDocument('da'), sdsDocument('en')]}});

      expect(component.otherSdsDocuments.map((d) => d.language)).toEqual(['da', 'sv']);
      component.openSdsDocument(component.otherSdsDocuments[0]);

      expect(getSds).toHaveBeenCalledWith('md5da');
    });

    it('lists a language once with the newest sheet and never the user\'s own language', () => {
      const {component} = build(true, undefined, {lang: 'en-US', entry: {sdsDocuments: [
        sdsDocument('sv', {fileName: 'sv-old', revisionDate: '2025-01-01T00:00:00Z'}),
        sdsDocument('sv', {fileName: 'sv-new', revisionDate: '2026-01-01T00:00:00Z'}),
        sdsDocument('en', {fileName: 'en-a'}), sdsDocument('en', {fileName: 'en-b'}),
      ]}});

      expect(component.otherSdsDocuments.map((d) => d.fileName)).toEqual(['sv-new']);
    });

    it('opens the one label directly', () => {
      const {component, getLabelBlob} = build(true, undefined, {entry: {labels: [labelOf({fileName: 'lab1'})]}});

      expect(component.labels).toHaveLength(1);
      component.openLabel();

      expect(getLabelBlob).toHaveBeenCalledWith('lab1');
    });

    it('lists several labels and opens the chosen one', () => {
      const {component, getLabelBlob} = build(true, undefined, {entry: {labels: [labelOf({fileName: 'lab1', packaging: '5 L'}), labelOf({fileName: 'lab2', kind: ChemicalLabelKind.MinorUse})]}});

      expect(component.labels.map((l) => l.fileName)).toEqual(['lab1', 'lab2']);
      component.openLabelFile(component.labels[1]);

      expect(getLabelBlob).toHaveBeenCalledWith('lab2');
    });

    it('has no labels for old data', () => {
      const {component, getLabelBlob} = build(true);

      expect(component.labels).toEqual([]);
      component.openLabel();

      expect(getLabelBlob).not.toHaveBeenCalled();
    });
  });
});
