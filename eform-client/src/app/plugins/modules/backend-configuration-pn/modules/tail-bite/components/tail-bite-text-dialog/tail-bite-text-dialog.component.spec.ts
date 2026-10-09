import {TestBed} from '@angular/core/testing';
import {Overlay} from '@angular/cdk/overlay';
import {MAT_DIALOG_DATA, MatDialog, MatDialogRef} from '@angular/material/dialog';
import {Subject, of, throwError} from 'rxjs';
import {TailBiteTextDialogComponent, TailBiteTextDialogData, askText} from './tail-bite-text-dialog.component';

describe('TailBiteTextDialogComponent', () => {
  let close: jest.Mock;

  const create = (data: TailBiteTextDialogData) => {
    close = jest.fn();
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [{provide: MAT_DIALOG_DATA, useValue: data}, {provide: MatDialogRef, useValue: {close}}],
    });
    return TestBed.runInInjectionContext(() => new TailBiteTextDialogComponent());
  };

  it('requires a text and hands it on trimmed, without closing itself', () => {
    const c = create({title: 'Omdøb lokation', label: 'Name'});
    const saved: string[] = [];
    c.saved.subscribe((t) => saved.push(t));
    c.text = '   ';
    c.save();
    c.text = '  Sektion 4 ';
    c.save();
    expect(saved).toEqual(['Sektion 4']);
    expect(close).not.toHaveBeenCalled();
  });

  it('starts from the given value', () => {
    expect(create({title: 'Omdøb lokation', label: 'Name', value: 'Sti 309'}).text).toBe('Sti 309');
  });

  it('closes with nothing on cancel', () => {
    const c = create({title: 'Træk tilbage', label: 'Reason'});
    c.cancel();
    expect(close).toHaveBeenCalledWith();
  });

  describe('askText', () => {
    const overlay = {scrollStrategies: {reposition: () => ({})}} as unknown as Overlay;
    let saved: Subject<string>;
    let closed: Subject<void>;
    let ref: {componentInstance: {saved: Subject<string>}; close: jest.Mock; afterClosed: () => Subject<void>};
    let dialog: MatDialog;

    beforeEach(() => {
      saved = new Subject();
      closed = new Subject();
      ref = {componentInstance: {saved}, close: jest.fn(), afterClosed: () => closed};
      dialog = {open: jest.fn().mockReturnValue(ref)} as unknown as MatDialog;
    });

    it('opens the text dialog, runs the action with the saved text, closes and emits once on success', () => {
      const action = jest.fn().mockReturnValue(of({success: true}));
      const done = jest.fn();
      askText(dialog, overlay, {title: 'Ny', label: 'Name'}, action).subscribe(done);
      expect((dialog.open as jest.Mock).mock.calls[0][0]).toBe(TailBiteTextDialogComponent);
      saved.next('Sti 309');
      expect(action).toHaveBeenCalledWith('Sti 309');
      expect(ref.close).toHaveBeenCalled();
      expect(done).toHaveBeenCalledTimes(1);
    });

    it('keeps the dialog (and the typed text) open when the action is refused or fails, and lets the user retry', () => {
      const action = jest.fn()
        .mockReturnValueOnce(of({success: false}))
        .mockReturnValueOnce(throwError(() => new Error('down')))
        .mockReturnValueOnce(of({success: true}));
      const done = jest.fn();
      const failed = jest.fn();
      askText(dialog, overlay, {title: 'Ny', label: 'Name'}, action).subscribe({next: done, error: failed});
      saved.next('Sti 309');
      saved.next('Sti 309');
      expect(ref.close).not.toHaveBeenCalled();
      saved.next('Sti 310');
      expect(action).toHaveBeenCalledTimes(3);
      expect(ref.close).toHaveBeenCalledTimes(1);
      expect(done).toHaveBeenCalledTimes(1);
      expect(failed).not.toHaveBeenCalled();
    });

    it('completes without running the action when cancelled', () => {
      const action = jest.fn();
      const done = jest.fn();
      const complete = jest.fn();
      askText(dialog, overlay, {title: 'Ny', label: 'Name'}, action).subscribe({next: done, complete});
      closed.next();
      saved.next('Sti 309');
      expect(action).not.toHaveBeenCalled();
      expect(done).not.toHaveBeenCalled();
      expect(complete).toHaveBeenCalled();
    });
  });
});
