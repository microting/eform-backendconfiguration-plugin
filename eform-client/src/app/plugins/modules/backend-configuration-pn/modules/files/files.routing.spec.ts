import {describe, it, expect, vi} from 'vitest';
// ng2-pdf-viewer assigns to the pdfjs-dist module namespace when it loads, which
// throws under the unit-test builder (ESM namespaces are not extensible), and
// pdfjs-dist cannot be mocked from inside the package. No spec renders a PDF.
vi.mock('ng2-pdf-viewer', () => ({PdfViewerModule: class {}, PdfViewerComponent: class {}}));
import {Route} from '@angular/router';
import {CanManageGuard} from './guards/can-manage.guard';

import {routes} from './files.routing';

function findRoute(path: string): Route | undefined {
  return routes.find(r => r.path === path);
}

describe('files routing', () => {
  // Exact arrays: the API's InboxController is [ArchiveManager] (admin or Leder), so the pages are closed to
  // everyone else with exactly CanManageGuard, which reads GET archive/capabilities.
  it.each(['inbox', 'settings'])('%s is for admins and Leders', path => {
    expect(findRoute(path)?.canActivate).toEqual([CanManageGuard]);
  });

  it.each(['', 'create'])('%s is not gated', path => {
    const route = findRoute(path);
    expect(route).toBeDefined();
    expect(route?.canActivate).toBeUndefined();
  });
});
