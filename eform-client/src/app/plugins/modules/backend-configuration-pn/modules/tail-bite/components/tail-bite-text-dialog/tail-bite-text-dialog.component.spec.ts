import {TestBed} from '@angular/core/testing';
import {Overlay} from '@angular/cdk/overlay';
import {MAT_DIALOG_DATA, MatDialog, MatDialogRef} from '@angular/material/dialog';
import {of} from 'rxjs';
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

  it('requires a text and returns it trimmed', () => {
    const c = create({title: 'Omdøb lokation', label: 'Name'});
    c.text = '   ';
    c.save();
    expect(close).not.toHaveBeenCalled();
    c.text = '  Sektion 4 ';
    c.save();
    expect(close).toHaveBeenCalledWith('Sektion 4');
  });

  it('starts from the given value', () => {
    expect(create({title: 'Omdøb lokation', label: 'Name', value: 'Sti 309'}).text).toBe('Sti 309');
  });

  it('closes with nothing on cancel', () => {
    const c = create({title: 'Træk tilbage', label: 'Reason'});
    c.cancel();
    expect(close).toHaveBeenCalledWith();
  });

  it('askText emits the saved text and nothing on cancel', () => {
    const overlay = {scrollStrategies: {reposition: () => ({})}} as unknown as Overlay;
    const open = jest.fn()
      .mockReturnValueOnce({afterClosed: () => of(undefined)})
      .mockReturnValueOnce({afterClosed: () => of('Sti 309')});
    const seen: string[] = [];
    askText({open} as unknown as MatDialog, overlay, {title: 'Ny', label: 'Name'}).subscribe((t) => seen.push(t));
    askText({open} as unknown as MatDialog, overlay, {title: 'Ny', label: 'Name'}).subscribe((t) => seen.push(t));
    expect(seen).toEqual(['Sti 309']);
    expect(open.mock.calls[0][0]).toBe(TailBiteTextDialogComponent);
  });
});
