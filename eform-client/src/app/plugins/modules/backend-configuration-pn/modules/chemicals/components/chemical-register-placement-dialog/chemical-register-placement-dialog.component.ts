import {Component, inject} from '@angular/core';
import {MAT_DIALOG_DATA, MatDialogRef} from '@angular/material/dialog';
import {finalize} from 'rxjs/operators';
import {ChemicalLocationModel, ChemicalRegisterEntryModel, ChemicalStockUnit} from '../../../../models';
import {BackendConfigurationPnChemicalsService} from '../../../../services';

const MIN_QUERY_LENGTH = 2;
const PAGE_SIZE = 25;

/** A barcode's digits without leading zeros (GTIN-13 vs GTIN-14), '' for anything that is not one. */
function barcodeDigits(value: string | null | undefined): string {
  const text = (value ?? '').trim();
  return /^\d{8,14}$/.test(text) ? text.replace(/^0+/, '') : '';
}

export interface ChemicalRegisterPlacementDialogData {
  propertyId: number;
  locations: ChemicalLocationModel[];
  stockEnabled: boolean;
}

/** W1 "Registrér placering": pick a BMD product from the register, a location, a note and (stock on) an initial amount. */
@Component({
  selector: 'app-chemical-register-placement-dialog',
  templateUrl: './chemical-register-placement-dialog.component.html',
  styleUrls: ['./chemical-register-placement-dialog.component.scss'],
  standalone: false,
})
export class ChemicalRegisterPlacementDialogComponent {
  private chemicalsService = inject(BackendConfigurationPnChemicalsService);
  private dialogRef = inject(MatDialogRef<ChemicalRegisterPlacementDialogComponent>);
  readonly data: ChemicalRegisterPlacementDialogData = inject(MAT_DIALOG_DATA);

  readonly units = ChemicalStockUnit;
  readonly locations = this.data.locations.filter((l) => !l.archived);
  query = '';
  results: ChemicalRegisterEntryModel[] = [];
  /** Matches the register has in all; it answers the first PAGE_SIZE. */
  total = 0;
  searched = false;
  selected: ChemicalRegisterEntryModel | null = null;
  productId: number | null = null;
  locationId: number | null = this.locations.length === 1 ? this.locations[0].id : null;
  placementNote = '';
  amount: number | null = null;
  unit: ChemicalStockUnit = ChemicalStockUnit.L;
  saving = false;
  /** Only the newest search may fill the results (two quick searches can answer out of order). */
  private searchGeneration = 0;
  /** The query of the shown results, digits only: a barcode picks its own package (m-5). */
  private searchedBarcode = '';

  get canSave(): boolean {
    const amountValid = !this.data.stockEnabled || this.amount === null || this.amount > 0;
    return !!this.selected && this.locationId !== null && amountValid && !this.saving;
  }

  get hasMore(): boolean {
    return this.total > this.results.length;
  }

  search(): void {
    const query = this.query.trim();
    // The register needs two characters; a shorter query would only come back as an error.
    if (query.length < MIN_QUERY_LENGTH) {
      return;
    }
    // A new search drops the earlier pick: Save must never register a chemical the user no longer sees (I-2).
    this.selected = null;
    this.productId = null;
    const generation = ++this.searchGeneration;
    this.chemicalsService.searchRegister(query, 0, PAGE_SIZE).subscribe((res) => {
      if (generation !== this.searchGeneration) {
        return;
      }
      // A pick from the old results made while this search was pending is gone with them (N-m1).
      this.selected = null;
      this.productId = null;
      const page = res && res.success ? res.model : null;
      this.results = page?.entries ?? [];
      this.total = page?.total ?? 0;
      this.searchedBarcode = barcodeDigits(query);
      this.searched = true;
    });
  }

  select(entry: ChemicalRegisterEntryModel): void {
    this.selected = entry;
    const scanned = this.searchedBarcode
      ? entry.products.find((p) => barcodeDigits(p.barcode) === this.searchedBarcode)
      : undefined;
    this.productId = scanned?.productId ?? null;
  }

  cancel(): void {
    this.dialogRef.close(false);
  }

  save(): void {
    if (!this.canSave) {
      return;
    }
    this.saving = true;
    this.chemicalsService.registerPlacement({
      locationId: this.locationId,
      chemicalId: this.selected.chemicalId,
      productId: this.productId,
      placementNote: this.placementNote.trim(),
      initialStock: this.data.stockEnabled && this.amount
        ? {containerSize: null, containerCount: null, amount: this.amount, unit: this.unit, batchLot: null, note: null, at: null}
        : null,
    }).pipe(finalize(() => (this.saving = false))).subscribe((res) => {
      if (res && res.success) {
        this.dialogRef.close(true);
      }
    });
  }
}
