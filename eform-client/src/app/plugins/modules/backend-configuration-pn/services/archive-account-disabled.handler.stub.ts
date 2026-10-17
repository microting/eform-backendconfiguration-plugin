import {ArchiveAccountDisabledHandler} from './archive-account-disabled.handler';

/**
 * Test-only (imported by the service specs, never by app code): an ArchiveAccountDisabledHandler whose `handle()`
 * passes the stream through. `handle` is a jest.fn, so a spec can assert every call was routed through it.
 */
export function passThroughAccountDisabledHandler(): {handler: ArchiveAccountDisabledHandler; handle: jest.Mock} {
  const handle = jest.fn(() => (source: any) => source);
  return {handler: {handle} as unknown as ArchiveAccountDisabledHandler, handle};
}
