import {describe, it, expect, afterEach, vi} from 'vitest';
import {NO_ERRORS_SCHEMA, Type} from '@angular/core';
import {ComponentFixture, TestBed} from '@angular/core/testing';
import {FormsModule} from '@angular/forms';
import {MAT_DIALOG_DATA, MatDialogRef} from '@angular/material/dialog';
import {MatRadioModule} from '@angular/material/radio';
import {NoopAnimationsModule} from '@angular/platform-browser/animations';
import {MtxSelectModule} from '@ng-matero/extensions/select';
import {TranslateModule} from '@ngx-translate/core';
import {ChemicalStockUnit} from '../../../models';
import {BackendConfigurationPnChemicalsService} from '../../../services';
import {discardConfirmHarness} from '../helpers/chemical-dialogs.testing';
import {ChemicalMoveDialogComponent} from './chemical-move-dialog/chemical-move-dialog.component';
import {ChemicalNoteDialogComponent} from './chemical-note-dialog/chemical-note-dialog.component';
import {ChemicalRemoveDialogComponent} from './chemical-remove-dialog/chemical-remove-dialog.component';
import {ChemicalStockDialogComponent} from './chemical-stock-dialog/chemical-stock-dialog.component';
import {detectChanges} from '../../../testing/detect-changes';

/** Template-level checks shared by the four W2 placement dialogs: busy Cancel, named radio groups, the move empty state. */
describe('Kemi placement dialogs (template)', () => {
  let fixture: ComponentFixture<unknown>;
  const find = (selector: string): HTMLElement | null => fixture.nativeElement.querySelector(selector);

  const row = {
    placement: {id: 7, locationId: 10, balance: 4, unit: ChemicalStockUnit.L, placementNote: 'Palle'},
    productName: 'Karate', stockUnitLabel: 'L', entry: {useAndPossessionDeadline: null},
  };
  const locations = [{id: 10, name: 'Lade', archived: false}, {id: 11, name: 'Kemirum', archived: false}];

  function render<T extends {saving: boolean}>(component: Type<T>, data: unknown): T {
    TestBed.configureTestingModule({
      imports: [component, FormsModule, MatRadioModule, MtxSelectModule, NoopAnimationsModule, TranslateModule.forRoot()],
      providers: [
        {provide: BackendConfigurationPnChemicalsService, useValue: {}},
        {provide: MatDialogRef, useValue: {close: vi.fn()}},
        ...discardConfirmHarness({translate: false}).providers,
        {provide: MAT_DIALOG_DATA, useValue: data},
      ],
      schemas: [NO_ERRORS_SCHEMA],
    });
    const created = TestBed.createComponent(component);
    fixture = created;
    detectChanges(created);
    return created.componentInstance;
  }

  afterEach(() => fixture?.destroy());

  const dialogs: Array<[string, Type<{saving: boolean}>, unknown, string]> = [
    ['move', ChemicalMoveDialogComponent, {row, locations, stockEnabled: true}, '#chemicalMoveCancelBtn'],
    ['note', ChemicalNoteDialogComponent, {row}, '#chemicalNoteCancelBtn'],
    ['stock', ChemicalStockDialogComponent, {row}, '#chemicalStockCancelBtn'],
    ['remove', ChemicalRemoveDialogComponent, {row, today: '2026-09-29'}, '#chemicalRemoveCancelBtn'],
  ];

  it.each(dialogs)('the %s dialog disables Cancel while its save is in flight (M2)', (_name, component, data, cancelId) => {
    const dialog = render(component, data);
    expect((find(cancelId) as HTMLButtonElement).disabled).toBe(false);

    dialog.saving = true;
    detectChanges(fixture);

    expect((find(cancelId) as HTMLButtonElement).disabled).toBe(true);
  });

  it.each([
    ['move (all or part)', ChemicalMoveDialogComponent, {row, locations, stockEnabled: true}, '#chemicalMoveAmountChoice'],
    ['stock (kind)', ChemicalStockDialogComponent, {row}, '#chemicalStockKind'],
    ['stock (unit)', ChemicalStockDialogComponent, {row}, '#chemicalStockUnit'],
    ['remove (reason)', ChemicalRemoveDialogComponent, {row, today: '2026-09-29'}, '#chemicalRemoveReason'],
  ] as Array<[string, Type<{saving: boolean}>, unknown, string]>)('names the %s radio group for assistive technology (M9)', (_name, component, data, groupId) => {
    render(component, data);

    expect(find(groupId)?.getAttribute('aria-label')).toBeTruthy();
  });

  it('tells the user when the property has no other active location (M4)', () => {
    render(ChemicalMoveDialogComponent, {row, locations: [locations[0]], stockEnabled: false});

    expect(find('#chemicalMoveNoTargets')?.textContent).toContain('Chemicals: No other location');
  });

  it.each([
    [1.0005, true],
    [1_000_001, true],
    [2.5, false],
    [1_000_000, false],
  ])('the stock dialog explains the amount limits (3 decimals, at most 1,000,000) for %s (Task 13 N-m3)', (amount, shown) => {
    const dialog = render(ChemicalStockDialogComponent, {row}) as unknown as {amount: number | null};

    dialog.amount = amount;
    detectChanges(fixture);

    const hint = find('#chemicalStockAmountHint');
    expect(hint !== null).toBe(shown);
    if (shown) {
      expect(hint?.textContent).toContain('Chemicals: Amount limits');
    }
  });

  it('shows no empty-state text when another location exists', () => {
    render(ChemicalMoveDialogComponent, {row, locations, stockEnabled: false});

    expect(find('#chemicalMoveNoTargets')).toBeNull();
  });
});
