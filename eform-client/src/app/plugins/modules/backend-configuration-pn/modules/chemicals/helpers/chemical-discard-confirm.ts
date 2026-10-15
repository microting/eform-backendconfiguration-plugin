import {Overlay} from '@angular/cdk/overlay';
import {MatDialog} from '@angular/material/dialog';
import {Observable, defer, map, merge, take, tap} from 'rxjs';
import {dialogConfigHelper} from 'src/app/common/helpers';
import {DeleteModalSettingModel} from 'src/app/common/models';
import {DeleteModalComponent} from 'src/app/common/modules/eform-shared/components';

/** Texts and ids for a "discard unsaved changes?" confirm; texts already translated. */
export interface ChemicalDiscardConfirm {
  headerText: string;
  fields: Array<{header: string; text: string}>;
  discardText: string;
  keepText: string;
  /** Id of the discard button; the keep-editing button gets the same id + "Cancel". */
  confirmId: string;
}

/**
 * Asks with the platform's DeleteModalComponent (the plugin's confirm style; its
 * red button suits a discard). Emits once: true after "discard" (and closes the
 * dialog), false when the dialog closes otherwise. Opens on subscribe.
 */
export function confirmDiscard(dialog: MatDialog, overlay: Overlay, c: ChemicalDiscardConfirm): Observable<boolean> {
  return defer(() => {
    const settings: DeleteModalSettingModel = {
      model: {},
      settings: {
        headerText: c.headerText,
        fields: c.fields.map((f) => ({header: f.header, field: '', type: 'text', text: f.text})),
        deleteButtonText: c.discardText,
        cancelButtonText: c.keepText,
        deleteButtonId: c.confirmId,
        cancelButtonId: `${c.confirmId}Cancel`,
      },
    };
    const ref = dialog.open(DeleteModalComponent, dialogConfigHelper(overlay, settings));
    return merge(
      ref.componentInstance.delete.pipe(map(() => true)),
      ref.afterClosed().pipe(map(() => false)),
    ).pipe(
      take(1),
      tap((discard) => {
        if (discard) {
          ref.close();
        }
      }),
    );
  });
}
