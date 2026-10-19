import {vi} from 'vitest';

/**
 * Captures what file-saver's `saveAs(blob, fileName)` saves: every call of the returned
 * mock is `(blob, fileName)`, made synchronously with the save.
 * `vi.mock('file-saver')` does not work under Angular's unit-test builder (ESM bundles),
 * so the capture sits on the browser APIs file-saver uses: it sets `<a>.download`, then
 * calls `URL.createObjectURL` for the Blob, and clicks the anchor later on a timer.
 * Call `restore()` when the test is done.
 */
export function captureSaveAs() {
  const saveAs = vi.fn<(blob: Blob, fileName: string) => void>();
  const originalCreate = (URL as any).createObjectURL;
  const originalRevoke = (URL as any).revokeObjectURL;

  let fileName = '';
  const downloadSetter = vi
    .spyOn(HTMLAnchorElement.prototype, 'download', 'set')
    .mockImplementation((name: string) => {
      fileName = name;
    });
  (URL as any).createObjectURL = vi.fn((blob: Blob) => {
    saveAs(blob, fileName);
    return 'blob:stub';
  });
  (URL as any).revokeObjectURL = vi.fn();
  // file-saver clicks the anchor on a timer; there is nothing to navigate to here.
  const realDispatch = EventTarget.prototype.dispatchEvent;
  const dispatchSpy = vi
    .spyOn(EventTarget.prototype, 'dispatchEvent')
    .mockImplementation(function (this: EventTarget, event: Event) {
      if (this instanceof HTMLAnchorElement && event.type === 'click') {
        return true;
      }
      return realDispatch.call(this, event);
    });

  return {
    saveAs,
    restore: () => {
      dispatchSpy.mockRestore();
      downloadSetter.mockRestore();
      (URL as any).createObjectURL = originalCreate;
      // file-saver revokes the object URL on a 40 s timer that may outlive the test.
      (URL as any).revokeObjectURL = originalRevoke ?? (() => undefined);
    },
  };
}
