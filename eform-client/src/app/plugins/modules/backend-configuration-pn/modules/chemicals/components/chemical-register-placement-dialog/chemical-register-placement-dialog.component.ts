import {Component, inject} from '@angular/core';
import {MAT_DIALOG_DATA, MatDialogRef} from '@angular/material/dialog';
import {finalize} from 'rxjs/operators';
import {ChemicalLocationModel, ChemicalRegisterEntryModel, ChemicalStockUnit} from '../../../../models';
import {BackendConfigurationPnChemicalsService} from '../../../../services';

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
  searched = false;
  selected: ChemicalRegisterEntryModel | null = null;
  productId: number | null = null;
  locationId: number | null = this.locations.length === 1 ? this.locations[0].id : null;
  placementNote = '';
  amount: number | null = null;
  unit: ChemicalStockUnit = ChemicalStockUnit.L;
  saving = false;

  get canSave(): boolean {
    return !!this.selected && this.locationId !== null && !this.saving;
  }

  search(): void {
    const query = this.query.trim();
    if (!query) {
      return;
    }
    this.chemicalsService.searchRegister(query, 0, 25).subscribe((res) => {
      this.results = (res && res.success && res.model?.entries) || [];
      this.searched = true;
    });
  }

  select(entry: ChemicalRegisterEntryModel): void {
    this.selected = entry;
    this.productId = null;
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
