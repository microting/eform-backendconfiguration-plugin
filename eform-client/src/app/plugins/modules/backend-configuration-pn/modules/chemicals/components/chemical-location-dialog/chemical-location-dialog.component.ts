import {Component, OnDestroy, inject} from '@angular/core';
import {MAT_DIALOG_DATA, MatDialogRef} from '@angular/material/dialog';
import {Observable, of} from 'rxjs';
import {switchMap} from 'rxjs/operators';
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
 * refused the dialog stays open as an edit of the saved location, so only the photo is still unsaved
 * and the next Save updates the location instead of creating it again.
 */
@Component({
  selector: 'app-chemical-location-dialog',
  templateUrl: './chemical-location-dialog.component.html',
  styleUrls: ['./chemical-location-dialog.component.scss'],
  standalone: false,
})
export class ChemicalLocationDialogComponent implements OnDestroy {
  private chemicalsService = inject(BackendConfigurationPnChemicalsService);
  private dialogRef = inject(MatDialogRef<ChemicalLocationDialogComponent, boolean>);
  private discardContext = discardContext(this.dialogRef);
  readonly data: ChemicalLocationDialogData = inject(MAT_DIALOG_DATA);

  /** The saved name and description: what Cancel compares with. */
  private savedName = this.data.row?.location.name ?? '';
  private savedDescription = this.data.row?.location.description ?? '';
  name = this.savedName;
  description = this.savedDescription;
  readonly openCount = this.data.row?.openCount ?? 0;
  photo: File | null = null;
  photoError: 'type' | 'size' | null = null;
  /** Object URL of the picked photo, shown instead of the stored one until it is saved. */
  previewUrl: string | null = null;
  saving = false;
  /** A Save already wrote the location (its photo may have been refused): the page must reload on close. */
  private wrote = false;
  /** The location this dialog writes: the edited one, or the one a first Save created. */
  private locationId: number | null = this.data.row?.location.id ?? null;

  /** An existing location, or a new one already created by a Save whose photo was refused. */
  get editing(): boolean {
    return this.locationId !== null;
  }

  get canSave(): boolean {
    return this.name.trim().length > 0 && !this.photoError && !this.saving;
  }

  get dirty(): boolean {
    return this.name.trim() !== this.savedName.trim()
      || this.description.trim() !== this.savedDescription.trim()
      || this.photo !== null;
  }

  /** Reads the picked file and clears the input, so picking the same file again fires a change. */
  onFileInput(input: HTMLInputElement): void {
    this.onFile(input.files?.[0] ?? null);
    input.value = '';
  }

  onFile(file: File | null): void {
    this.photo = file;
    this.photoError = file ? validateLocationPhoto(file) : null;
    this.setPreview(file && !this.photoError ? URL.createObjectURL(file) : null);
  }

  cancel(): void {
    cancelChemicalDialog(this.discardContext, {
      busy: this.saving,
      dirty: this.dirty,
      field: {headerKey: 'Location', text: this.name.trim() || this.savedName},
      confirmId: 'chemicalLocationDiscardConfirm',
      // A location saved before a refused photo is new or changed on the page, so it reloads (N-m2).
      result: this.wrote,
    });
  }

  save(): void {
    if (!this.canSave) {
      return;
    }
    this.saving = true;
    const name = this.name.trim();
    const description = this.description.trim();
    const photo = this.photo;
    const saved$: Observable<OperationDataResult<ChemicalLocationModel>> = this.locationId !== null
      ? this.chemicalsService.updateLocation(this.locationId, {name, description, sortOrder: null})
      : this.chemicalsService.createLocation({propertyId: this.data.propertyId, name, description, sortOrder: null});
    closeWhenSaved(this.dialogRef, saved$.pipe(
      switchMap((res) => {
        if (!res?.success) {
          return of(res);
        }
        this.wrote = true;
        this.locationId ??= res.model.id;
        this.savedName = name;
        this.savedDescription = description;
        return photo ? this.chemicalsService.uploadLocationPhoto(this.locationId, photo) : of(res);
      }),
    ), () => (this.saving = false));
  }

  ngOnDestroy(): void {
    this.setPreview(null);
  }

  private setPreview(url: string | null): void {
    if (this.previewUrl) {
      URL.revokeObjectURL(this.previewUrl);
    }
    this.previewUrl = url;
  }
}
