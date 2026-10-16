import {Subject, of, throwError} from 'rxjs';
import {closeWhenSaved, openSafetyDataSheet} from './chemical-dialogs.helper';

describe('closeWhenSaved (shared save/busy/close of the Kemi dialogs)', () => {
  it('closes with true after a successful save and reports when the request settles', () => {
    const dialogRef = {close: jest.fn()};
    const settled = jest.fn();
    const answer = new Subject<{success: boolean}>();

    closeWhenSaved(dialogRef, answer, settled);
    expect(settled).not.toHaveBeenCalled();
    answer.next({success: true});
    answer.complete();

    expect(dialogRef.close).toHaveBeenCalledWith(true);
    expect(settled).toHaveBeenCalledTimes(1);
  });

  it.each([
    ['a refused save', of({success: false})],
    ['no answer', of(null)],
  ])('stays open after %s and still frees the dialog', (_name, answer) => {
    const dialogRef = {close: jest.fn()};
    const settled = jest.fn();

    closeWhenSaved(dialogRef, answer, settled);

    expect(dialogRef.close).not.toHaveBeenCalled();
    expect(settled).toHaveBeenCalledTimes(1);
  });
});

describe('openSafetyDataSheet (shared by W1 and the W2 side panel)', () => {
  const toastr = {error: jest.fn()};
  const translate = {instant: (key: string) => key};

  beforeEach(() => toastr.error.mockClear());

  it('opens the fetched sheet in a new tab', () => {
    const open = jest.spyOn(window, 'open').mockReturnValue(null);
    const createObjectURL = jest.fn().mockReturnValue('blob:sds');
    Object.defineProperty(URL, 'createObjectURL', {value: createObjectURL, configurable: true});
    const sds = {getSdsBlob: jest.fn().mockReturnValue(of(new Blob(['%PDF'])))};

    openSafetyDataSheet(sds, toastr, translate, 'sds200');

    expect(sds.getSdsBlob).toHaveBeenCalledWith('sds200');
    expect(open).toHaveBeenCalledWith('blob:sds', '_blank');
    expect(toastr.error).not.toHaveBeenCalled();
    open.mockRestore();
  });

  it('toasts a fixed localized text when the sheet cannot be fetched (the host shows nothing for a blob error)', () => {
    const sds = {getSdsBlob: jest.fn().mockReturnValue(throwError(() => new Error('404 technical')))};

    openSafetyDataSheet(sds, toastr, translate, 'sds200');

    expect(toastr.error).toHaveBeenCalledWith('Chemicals: Could not open safety data sheet');
  });
});
