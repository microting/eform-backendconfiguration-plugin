import {describe, it, expect, vi} from 'vitest';
// ng2-pdf-viewer assigns to the pdfjs-dist module namespace when it loads, which
// throws under the unit-test builder (ESM namespaces are not extensible), and
// pdfjs-dist cannot be mocked from inside the package. No spec renders a PDF.
vi.mock('ng2-pdf-viewer', () => ({PdfViewerModule: class {}, PdfViewerComponent: class {}}));
import {Route} from '@angular/router';
import {IsAdminGuard} from 'src/app/common/guards';

import {routes} from './files.routing';

function findRoute(path: string): Route | undefined {
  return routes.find(r => r.path === path);
}

describe('files routing', () => {
  // Exact arrays: the API's InboxController is admin-only, so the pages must be
  // closed to everyone else with exactly IsAdminGuard.
  it.each(['inbox', 'settings'])('%s is admin-only', path => {
    expect(findRoute(path)?.canActivate).toEqual([IsAdminGuard]);
  });

  it.each(['', 'create'])('%s is not admin-gated', path => {
    const route = findRoute(path);
    expect(route).toBeDefined();
    expect(route?.canActivate).toBeUndefined();
  });
});
