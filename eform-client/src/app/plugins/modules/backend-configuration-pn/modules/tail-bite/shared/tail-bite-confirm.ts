import {Overlay} from '@angular/cdk/overlay';
import {MatDialog} from '@angular/material/dialog';
import {EMPTY, Observable, catchError, exhaustMap, filter, map, take, takeUntil, tap} from 'rxjs';
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
  return ref.componentInstance.delete.pipe(
    takeUntil(ref.afterClosed()),
    exhaustMap(() => action().pipe(catchError(() => EMPTY))),
    filter((res) => !!res?.success),
    take(1),
    tap(() => ref.close()),
    map(() => undefined),
  );
}
