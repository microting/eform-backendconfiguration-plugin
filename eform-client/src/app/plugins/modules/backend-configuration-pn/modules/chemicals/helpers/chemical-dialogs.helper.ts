import {Overlay} from '@angular/cdk/overlay';
import {inject} from '@angular/core';
import {MatDialog} from '@angular/material/dialog';
import {TranslateService} from '@ngx-translate/core';
import {Observable, finalize} from 'rxjs';
import {confirmDiscardWhenDirty} from '../chemicals-unsaved-changes';
import {confirmDiscard} from './chemical-discard-confirm';
import {ChemicalInventoryRow} from './chemical-inventory.helper';

/** Data of every W2 placement dialog; move and remove add their own fields. */
export interface ChemicalPlacementDialogData {
  row: ChemicalInventoryRow;
}

/** What the dialogs need of a MatDialogRef (keeps the helpers testable without one). */
export interface ChemicalClosable {
  close(result?: boolean): void;
}

/**
 * The save of every Kemi dialog: closes with true once the server accepted it, stays open on a
 * refusal (200 + success=false: the host's ApiBaseService toasts the message) or no answer, and
 * reports `settled` either way so the dialog frees its busy state.
 */
export function closeWhenSaved(
  dialogRef: ChemicalClosable,
  request: Observable<{success: boolean} | null>,
  settled: () => void,
): void {
  request.pipe(finalize(settled)).subscribe((res) => {
    if (res && res.success) {
      dialogRef.close(true);
    }
  });
}

/** The services a Kemi dialog's discard confirm needs. */
export interface ChemicalDiscardContext {
  dialogRef: ChemicalClosable;
  dialog: MatDialog;
  overlay: Overlay;
  translate: {instant(key: string): string};
}

/** Injects what a discard confirm needs; call from a dialog's field initializer (injection context). */
export function discardContext(dialogRef: ChemicalClosable): ChemicalDiscardContext {
  return {dialogRef, dialog: inject(MatDialog), overlay: inject(Overlay), translate: inject(TranslateService)};
}

/**
 * Cancel of a Kemi dialog with typed input (M5): nothing while a save is in flight (it cannot be
 * cancelled), at once when nothing was entered, otherwise only after the user confirms the discard.
 * The confirm names what is edited (`field`: a translation key and its value). The dialog closes
 * with `result` (true when something was already saved, so the page reloads).
 */
export function cancelChemicalDialog(
  ctx: ChemicalDiscardContext,
  state: {busy: boolean; dirty: boolean; field: {headerKey: string; text: string}; confirmId: string; result?: boolean},
): void {
  const t = (key: string) => ctx.translate.instant(key);
  confirmDiscardWhenDirty(state.busy, state.dirty, () => confirmDiscard(ctx.dialog, ctx.overlay, {
    headerText: t('Chemicals: Discard unsaved changes?'),
    fields: [{header: t(state.field.headerKey), text: state.field.text}],
    discardText: t('Chemicals: Discard changes'),
    keepText: t('Chemicals: Keep editing'),
    confirmId: state.confirmId,
  })).subscribe((discard) => {
    if (discard) {
      ctx.dialogRef.close(state.result ?? false);
    }
  });
}

/** Cancel of a W2 placement dialog: the confirm names the product. */
export function cancelPlacementDialog(
  ctx: ChemicalDiscardContext,
  state: {busy: boolean; dirty: boolean; productName: string; confirmId: string},
): void {
  cancelChemicalDialog(ctx, {
    busy: state.busy, dirty: state.dirty, confirmId: state.confirmId,
    field: {headerKey: 'Chemicals: Product', text: state.productName},
  });
}

/**
 * Opens a safety data sheet in a new tab (W1 row menu and the W2 side panel). The blob request runs
 * through the host interceptors, which show nothing for a failed blob, so a failure is toasted here
 * with a fixed text (never the technical error).
 */
export function openSafetyDataSheet(
  sds: {getSdsBlob(fileName: string): Observable<Blob>},
  toastr: {error(message: string): unknown},
  translate: {instant(key: string): string},
  fileName: string,
): void {
  sds.getSdsBlob(fileName).subscribe({
    next: (blob) => window.open(URL.createObjectURL(blob), '_blank'),
    error: () => toastr.error(translate.instant('Chemicals: Could not open safety data sheet')),
  });
}
