import {Overlay} from '@angular/cdk/overlay';
import {MatDialog, MatDialogRef} from '@angular/material/dialog';
import {EMPTY, MonoTypeOperatorFunction, Observable, catchError, exhaustMap, filter, map, take, takeUntil, tap} from 'rxjs';
import {dialogConfigHelper} from 'src/app/common/helpers';
import {DeleteModalSettingModel, OperationResult} from 'src/app/common/models';
import {DeleteModalComponent} from 'src/app/common/modules/eform-shared/components';

/** Texts for a confirmation; all already translated. */
export interface TailBiteConfirm {
  headerText: string;
  itemLabel: string;
  itemName: string;
  confirmText: string;
  /** Id of the confirm button; the cancel button gets the same id + "Cancel". */
  confirmId: string;
}

/**
 * Asks with the platform's DeleteModalComponent and runs `action` on confirm. Emits once, after the action
 * succeeded and the dialog closed; completes without emitting when the user cancels. A refused action leaves
 * the dialog open (the toast shows the server's message) so the user can cancel or retry; so does a failed
 * request, whose error is absorbed here so the confirm button keeps working.
 */
export function openConfirm(dialog: MatDialog, overlay: Overlay, c: TailBiteConfirm,
  action: () => Observable<OperationResult>): Observable<void> {
  const settings: DeleteModalSettingModel = {
    model: {},
    settings: {
      headerText: c.headerText,
      fields: [{header: c.itemLabel, field: 'name', type: 'text', text: c.itemName}],
      deleteButtonText: c.confirmText,
      deleteButtonId: c.confirmId,
      cancelButtonId: `${c.confirmId}Cancel`,
    },
  };
  const ref = dialog.open(DeleteModalComponent, dialogConfigHelper(overlay, settings));
  return closeOnSuccess(ref, ref.componentInstance.delete, action);
}

/**
 * The dialog flow openConfirm and askText share: each `trigger$` value runs `action` (one at a time; clicks while it runs
 * are ignored). Emits once, after the first accepted action, and closes the dialog; completes without emitting when the
 * dialog closes first. A refused or failed action leaves the dialog open; a failed request's error is absorbed.
 */
export function closeOnSuccess<T>(ref: MatDialogRef<unknown>, trigger$: Observable<T>,
  action: (value: T) => Observable<OperationResult>): Observable<void> {
  return trigger$.pipe(
    takeUntil(ref.afterClosed()),
    exhaustMap((value) => action(value).pipe(catchError(() => EMPTY))),
    filter((res) => !!res?.success),
    take(1),
    tap(() => ref.close()),
    map(() => undefined),
  );
}

/** Runs `onRefused` when the server refused the call or the request failed; the result or error passes through unchanged. */
export function whenRefused(onRefused: () => void): MonoTypeOperatorFunction<OperationResult> {
  return tap({
    next: (res) => {
      if (!res?.success) {
        onRefused();
      }
    },
    error: () => onRefused(),
  });
}
