import {Component, inject} from '@angular/core';
import {MAT_DIALOG_DATA, MatDialogRef} from '@angular/material/dialog';
import {Observable, of} from 'rxjs';
import {switchMap, tap} from 'rxjs/operators';
import {OperationDataResult} from 'src/app/common/models';
import {ChemicalLocationModel} from '../../../../models';
import {BackendConfigurationPnChemicalsService} from '../../../../services';
import {ChemicalLocationRow, cancelChemicalDialog, closeWhenSaved, discardContext, validateLocationPhoto} from '../../helpers';

export interface ChemicalLocationDialogData {
  propertyId: number;
  /** null = "Ny lokation". */
  row: ChemicalLocationRow | null;
  photoUrl: string | null;
}

/**
 * W3 "Rediger lokation" / "Ny lokation": name, description and a photo (a new photo replaces the old
 * one; Plan E decision 10). The photo is uploaded only after the location was saved. When the upload is
 * refused the dialog stays open, and the next Save updates the location it already created.
 */
@Component({
  selector: 'app-chemical-location-dialog',
  templateUrl: './chemical-location-dialog.component.html',
  styleUrls: ['./chemical-location-dialog.component.scss'],
  standalone: false,
})
export class ChemicalLocationDialogComponent {
  private chemicalsService = inject(BackendConfigurationPnChemicalsService);
  private dialogRef = inject(MatDialogRef<ChemicalLocationDialogComponent, boolean>);
  private discardContext = discardContext(this.dialogRef);
  readonly data: ChemicalLocationDialogData = inject(MAT_DIALOG_DATA);

  private readonly originalName = this.data.row?.location.name ?? '';
  private readonly originalDescription = this.data.row?.location.description ?? '';
  name = this.originalName;
  description = this.originalDescription;
  readonly openCount = this.data.row?.openCount ?? 0;
  photo: File | null = null;
  photoError: 'type' | 'size' | null = null;
  saving = false;
  /** The location this dialog writes: the edited one, or the one a first Save created. */
  private locationId: number | null = this.data.row?.location.id ?? null;

  get canSave(): boolean {
    return this.name.trim().length > 0 && !this.photoError && !this.saving;
  }

  get dirty(): boolean {
    return this.name.trim() !== this.originalName.trim()
      || this.description.trim() !== this.originalDescription.trim()
      || this.photo !== null;
  }

  onFile(file: File | null): void {
    this.photo = file;
    this.photoError = file ? validateLocationPhoto(file) : null;
  }

  cancel(): void {
    cancelChemicalDialog(this.discardContext, {
      busy: this.saving,
      dirty: this.dirty,
      field: {headerKey: 'Location', text: this.name.trim() || this.originalName},
      confirmId: 'chemicalLocationDiscardConfirm',
      // A location created before a refused photo is new on the page, so it reloads.
      result: this.data.row === null && this.locationId !== null,
    });
  }

  save(): void {
    if (!this.canSave) {
      return;
    }
    this.saving = true;
    const name = this.name.trim();
    const description = this.description.trim();
    const saved$: Observable<OperationDataResult<ChemicalLocationModel>> = this.locationId !== null
      ? this.chemicalsService.updateLocation(this.locationId, {name, description, sortOrder: null})
      : this.chemicalsService.createLocation({propertyId: this.data.propertyId, name, description, sortOrder: null});
    const photo = this.photo;
    closeWhenSaved(this.dialogRef, saved$.pipe(
      tap((res) => {
        if (res && res.success && this.locationId === null) {
          this.locationId = res.model.id;
        }
      }),
      switchMap((res) => res && res.success && photo
        ? this.chemicalsService.uploadLocationPhoto(this.locationId, photo)
        : of(res)),
    ), () => (this.saving = false));
  }
}
