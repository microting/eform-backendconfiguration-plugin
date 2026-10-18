import {HttpErrorResponse} from '@angular/common/http';
import {Subject, of, throwError} from 'rxjs';
import {closeWhenSaved, downloadFailureText, openSafetyDataSheet} from './chemical-dialogs.helper';

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

describe('downloadFailureText (shared by the W1 exports and the W6 Excel)', () => {
  const translate = {instant: (key: string) => key};

  it.each([
    ['the server\'s (English) reason for a refused file, after the localized prefix', {status: 400, message: 'The period ends before it starts.'}, 'Export failed: The period ends before it starts.'],
    ['the server\'s reason for a conflict, after the localized prefix', {status: 409, message: 'Search for at least two characters.'}, 'Export failed: Search for at least two characters.'],
    ['a refusal without a reason', {status: 409, message: null}, 'Export failed'],
    ['a forbidden file', {status: 403, message: null}, 'Chemicals: Export not allowed'],
    ['no answer', {status: 0, message: null}, 'Export failed'],
    ['a raw HttpErrorResponse (its message is technical)', new HttpErrorResponse({status: 400, url: '/api/x', error: 'boom'}), 'Export failed'],
    ['a forbidden raw HttpErrorResponse', new HttpErrorResponse({status: 403, url: '/api/x'}), 'Export failed'],
    ['an Error with a status', Object.assign(new Error('TypeError: technical detail'), {status: 400}), 'Export failed'],
    ['nothing at all', null, 'Export failed'],
    ['a string', 'boom', 'Export failed'],
  ])('says %s', (_name, error, expected) => {
    expect(downloadFailureText(error, translate)).toBe(expected);
  });
});
