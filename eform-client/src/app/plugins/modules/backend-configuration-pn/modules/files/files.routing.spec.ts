import {Route} from '@angular/router';
import {IsAdminGuard} from 'src/app/common/guards';

// The components barrel pulls in the host's module graph (see
// backend-configuration-pn.routing.spec.ts); this spec only inspects the
// route table, so mock it.
jest.mock('./components', () => ({
  FilesContainerComponent: class MockFilesContainerComponent {},
  FileCreateComponent: class MockFileCreateComponent {},
  InboxContainerComponent: class MockInboxContainerComponent {},
  InboxSettingsComponent: class MockInboxSettingsComponent {},
}));

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
