import {describe, it, expect, beforeEach, afterEach, vi} from 'vitest';
import {HttpErrorResponse} from '@angular/common/http';
import {EMPTY, Subject, of, throwError} from 'rxjs';
import {closeWhenSaved, downloadFailureText, openChemicalLabel, openSafetyDataSheet} from './chemical-dialogs.helper';

describe('closeWhenSaved (shared save/busy/close of the Kemi dialogs)', () => {
  it('closes with true after a successful save and reports when the request settles', () => {
    const dialogRef = {close: vi.fn()};
    const settled = vi.fn();
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
    const dialogRef = {close: vi.fn()};
    const settled = vi.fn();

    closeWhenSaved(dialogRef, answer, settled);

    expect(dialogRef.close).not.toHaveBeenCalled();
    expect(settled).toHaveBeenCalledTimes(1);
  });
});

describe.each([
  ['openSafetyDataSheet', 'getSdsBlob', 'Chemicals: Could not open safety data sheet',
    (api: any, toastr: any, translate: any, name: string) => openSafetyDataSheet(api, toastr, translate, name)],
  ['openChemicalLabel', 'getLabelBlob', 'Chemicals: Could not open label',
    (api: any, toastr: any, translate: any, name: string) => openChemicalLabel(api, toastr, translate, name)],
])('%s (shared by W1 and the W2 side panel)', (_name, method, failureKey, run) => {
  const toastr = {error: vi.fn(), info: vi.fn()};
  const translate = {instant: (key: string) => key};
  const api = (answer: unknown) => ({[method]: vi.fn().mockReturnValue(answer)});
  let createObjectURL: ReturnType<typeof vi.fn>;
  let revokeObjectURL: ReturnType<typeof vi.fn>;
  let tab: {location: {href: string}; close: ReturnType<typeof vi.fn>};

  beforeEach(() => {
    vi.useFakeTimers();
    toastr.error.mockClear();
    toastr.info.mockClear();
    createObjectURL = vi.fn().mockReturnValue('blob:pdf');
    revokeObjectURL = vi.fn();
    Object.defineProperty(URL, 'createObjectURL', {value: createObjectURL, configurable: true});
    Object.defineProperty(URL, 'revokeObjectURL', {value: revokeObjectURL, configurable: true});
    tab = {location: {href: ''}, close: vi.fn()};
  });
  afterEach(() => {
    vi.useRealTimers();
    vi.restoreAllMocks();
  });

  it('opens the tab synchronously in the click, before the download answers', () => {
    const open = vi.spyOn(window, 'open').mockReturnValue(tab as unknown as Window);
    const answer = new Subject<Blob>();

    run(api(answer), toastr, translate, 'file1');

    expect(open).toHaveBeenCalledWith('', '_blank');
    expect(tab.location.href).toBe('');
    answer.next(new Blob(['%PDF']));
    answer.complete();
    expect(tab.location.href).toBe('blob:pdf');
    expect(toastr.error).not.toHaveBeenCalled();
    expect(toastr.info).not.toHaveBeenCalled();
  });

  it('asks the service for the file', () => {
    vi.spyOn(window, 'open').mockReturnValue(tab as unknown as Window);
    const service = api(of(new Blob(['%PDF'])));

    run(service, toastr, translate, 'file1');

    expect((service as Record<string, ReturnType<typeof vi.fn>>)[method]).toHaveBeenCalledWith('file1');
  });

  it('revokes the object URL after about a minute', () => {
    vi.spyOn(window, 'open').mockReturnValue(tab as unknown as Window);

    run(api(of(new Blob(['%PDF']))), toastr, translate, 'file1');

    expect(revokeObjectURL).not.toHaveBeenCalled();
    vi.advanceTimersByTime(60_000);
    expect(revokeObjectURL).toHaveBeenCalledWith('blob:pdf');
  });

  it('falls back to a download with a toast when the pop-up is blocked', () => {
    vi.spyOn(window, 'open').mockReturnValue(null);
    const click = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function (this: HTMLAnchorElement) {
      expect(this.download).toBe('file1.pdf');
      expect(this.href).toContain('blob:pdf');
    });

    run(api(of(new Blob(['%PDF']))), toastr, translate, 'file1');

    expect(click).toHaveBeenCalledTimes(1);
    expect(toastr.info).toHaveBeenCalledWith('Chemicals: Pop-up blocked, file downloaded');
    expect(toastr.error).not.toHaveBeenCalled();
    vi.advanceTimersByTime(60_000);
    expect(revokeObjectURL).toHaveBeenCalledWith('blob:pdf');
  });

  it('closes the tab and toasts a fixed localized text when the file cannot be fetched', () => {
    vi.spyOn(window, 'open').mockReturnValue(tab as unknown as Window);

    run(api(throwError(() => new Error('404 technical'))), toastr, translate, 'file1');

    expect(tab.close).toHaveBeenCalledTimes(1);
    expect(toastr.error).toHaveBeenCalledTimes(1);
    expect(toastr.error).toHaveBeenCalledWith(failureKey);
    expect(createObjectURL).not.toHaveBeenCalled();
  });

  it('closes the tab and toasts the same text when the request completes without a file (the host ends a 5xx with EMPTY)', () => {
    vi.spyOn(window, 'open').mockReturnValue(tab as unknown as Window);

    run(api(EMPTY), toastr, translate, 'file1');

    expect(tab.close).toHaveBeenCalledTimes(1);
    expect(toastr.error).toHaveBeenCalledTimes(1);
    expect(toastr.error).toHaveBeenCalledWith(failureKey);
  });

  it('toasts the failure without a tab when the pop-up was blocked and the file cannot be fetched', () => {
    vi.spyOn(window, 'open').mockReturnValue(null);

    run(api(throwError(() => new Error('404'))), toastr, translate, 'file1');

    expect(toastr.error).toHaveBeenCalledWith(failureKey);
    expect(toastr.info).not.toHaveBeenCalled();
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
