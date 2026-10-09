import {EventEmitter} from '@angular/core';
import {MatDialog} from '@angular/material/dialog';
import {Overlay} from '@angular/cdk/overlay';
import {Subject, of, throwError} from 'rxjs';
import {DeleteModalComponent} from 'src/app/common/modules/eform-shared/components';
import {openConfirm, whenRefused} from './tail-bite-confirm';

describe('openConfirm', () => {
  const overlay = {scrollStrategies: {reposition: () => ({})}} as unknown as Overlay;
  const confirm = {headerText: 'Delete location?', itemLabel: 'Name', itemName: 'Sti 309', confirmText: 'Delete', confirmId: 'x'};
  let deleteClicks: EventEmitter<unknown>;
  let closed: Subject<void>;
  let ref: {componentInstance: {delete: EventEmitter<unknown>}; close: jest.Mock; afterClosed: () => Subject<void>};
  let dialog: {open: jest.Mock};

  beforeEach(() => {
    deleteClicks = new EventEmitter();
    closed = new Subject();
    ref = {componentInstance: {delete: deleteClicks}, close: jest.fn(), afterClosed: () => closed};
    dialog = {open: jest.fn().mockReturnValue(ref)};
  });

  it('opens the platform delete modal with the given texts', () => {
    openConfirm(dialog as unknown as MatDialog, overlay, confirm, () => of({success: true} as any)).subscribe();
    const [component, config] = dialog.open.mock.calls[0];
    expect(component).toBe(DeleteModalComponent);
    expect(config.data.settings).toEqual(expect.objectContaining({headerText: 'Delete location?', deleteButtonText: 'Delete',
      deleteButtonId: 'x', cancelButtonId: 'xCancel'}));
    expect(config.data.settings.fields[0]).toEqual({header: 'Name', field: 'name', type: 'text', text: 'Sti 309'});
  });

  it('runs the action on confirm, closes and emits once on success', () => {
    const action = jest.fn().mockReturnValue(of({success: true}));
    const done = jest.fn();
    openConfirm(dialog as unknown as MatDialog, overlay, confirm, action).subscribe(done);
    deleteClicks.emit({});
    expect(action).toHaveBeenCalledTimes(1);
    expect(ref.close).toHaveBeenCalled();
    expect(done).toHaveBeenCalledTimes(1);
  });

  it('keeps the dialog open when the server refuses, and lets the user retry', () => {
    const action = jest.fn().mockReturnValueOnce(of({success: false})).mockReturnValueOnce(of({success: true}));
    const done = jest.fn();
    openConfirm(dialog as unknown as MatDialog, overlay, confirm, action).subscribe(done);
    deleteClicks.emit({});
    expect(ref.close).not.toHaveBeenCalled();
    deleteClicks.emit({});
    expect(done).toHaveBeenCalledTimes(1);
  });

  it('keeps the dialog working after a failed request, so the user can retry', () => {
    const action = jest.fn()
      .mockReturnValueOnce(throwError(() => new Error('down')))
      .mockReturnValueOnce(of({success: true}));
    const done = jest.fn();
    const failed = jest.fn();
    openConfirm(dialog as unknown as MatDialog, overlay, confirm, action).subscribe({next: done, error: failed});
    deleteClicks.emit({});
    expect(ref.close).not.toHaveBeenCalled();
    deleteClicks.emit({});
    expect(action).toHaveBeenCalledTimes(2);
    expect(done).toHaveBeenCalledTimes(1);
    expect(failed).not.toHaveBeenCalled();
  });

  it('does nothing when cancelled', () => {
    const action = jest.fn();
    const done = jest.fn();
    openConfirm(dialog as unknown as MatDialog, overlay, confirm, action).subscribe(done);
    closed.next();
    deleteClicks.emit({});
    expect(action).not.toHaveBeenCalled();
    expect(done).not.toHaveBeenCalled();
  });
});

describe('whenRefused', () => {
  it('runs the callback on a refusal or a failed request, not on success, and passes the outcome through', () => {
    const refused = jest.fn();
    const seen: unknown[] = [];
    of({success: true} as any).pipe(whenRefused(refused)).subscribe((r) => seen.push(r));
    expect(refused).not.toHaveBeenCalled();
    of({success: false} as any).pipe(whenRefused(refused)).subscribe((r) => seen.push(r));
    expect(refused).toHaveBeenCalledTimes(1);
    const failed = jest.fn();
    throwError(() => new Error('down')).pipe(whenRefused(refused)).subscribe({error: failed});
    expect(refused).toHaveBeenCalledTimes(2);
    expect(failed).toHaveBeenCalled();
    expect(seen).toEqual([{success: true}, {success: false}]);
  });
});
