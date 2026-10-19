import {vi} from 'vitest';
import {Overlay} from '@angular/cdk/overlay';
import {MatDialog} from '@angular/material/dialog';
import {TranslateService} from '@ngx-translate/core';
import {Subject} from 'rxjs';

/**
 * Spec support for the Kemi placement dialogs (not exported from the helpers barrel; production
 * code never imports it): the providers a dialog's discard confirm needs, and a way to answer it.
 */
export function discardConfirmHarness(opts: {translate?: boolean} = {}) {
  let discarded = new Subject<void>();
  let closed = new Subject<void>();
  const dialog = {
    open: vi.fn().mockImplementation(() => {
      discarded = new Subject<void>();
      closed = new Subject<void>();
      return {componentInstance: {delete: discarded}, afterClosed: () => closed, close: vi.fn()};
    }),
  };
  return {
    dialog,
    providers: [
      {provide: MatDialog, useValue: dialog},
      {provide: Overlay, useValue: {scrollStrategies: {reposition: vi.fn().mockReturnValue({})}}},
      // A TestBed template spec brings the real TranslateModule instead.
      ...(opts.translate === false ? [] : [{provide: TranslateService, useValue: {instant: (key: string) => key}}]),
    ],
    /** Id of the discard button of the last confirm that was opened. */
    confirmId: (): string => dialog.open.mock.lastCall[1].data.settings.deleteButtonId,
    discard: () => discarded.next(),
    keepEditing: () => closed.next(),
  };
}
