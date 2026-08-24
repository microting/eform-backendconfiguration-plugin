import {Route} from '@angular/router';
import {AuthGuard, PermissionGuard} from 'src/app/common/guards';

// The plugin components barrel ('./components', imported by the routing
// module for its eagerly-referenced components) re-exports
// BackendConfigurationCaseModule, which transitively imports the host's
// entire src/app/modules barrel — that chain reaches
// admin-settings.component.ts and its ESM-only `uuid` dependency, which the
// host jest transform does not process (transformIgnorePatterns only lets
// *.mjs and an allowlist through). This spec only inspects the route
// definitions, so mock the barrel to keep the module graph scoped to the
// routing table. jest.mock is hoisted above the imports, so the real barrel
// is never loaded. Same treatment as report-table.component.spec.ts.
jest.mock('./components', () => ({
  GoogleDriveAccountsComponent: class MockGoogleDriveAccountsComponent {},
  GoogleDriveOAuthFinishComponent: class MockGoogleDriveOAuthFinishComponent {},
  PropertiesContainerComponent: class MockPropertiesContainerComponent {},
  PropertyAreasComponent: class MockPropertyAreasComponent {},
  ReportContainerComponent: class MockReportContainerComponent {},
}));

import {routes} from './backend-configuration-pn.routing';

// Depth-first lookup by path. Indices shift whenever a route is added, so the
// route under test is always located by its `path`, never by position.
function findRoute(routeList: Route[], path: string): Route | undefined {
  for (const route of routeList ?? []) {
    if (route.path === path) {
      return route;
    }
    const match = findRoute(route.children ?? [], path);
    if (match) {
      return match;
    }
  }
  return undefined;
}

describe('BackendConfigurationPnRouting', () => {
  it('exposes the plugin routes with a single root route', () => {
    expect(routes.length).toBe(1);
    expect(routes[0].children?.length).toBeGreaterThan(0);
  });

  describe('adhoc-tasks route', () => {
    // Ad-hoc tasks are intentionally open to every user of this plugin
    // (spec 2026-08-24-adhoc-tasks-unrestricted-access-design). The
    // adhoc_enable claim was never granted to non-admin roles and is enforced
    // nowhere server-side, so PermissionGuard silently cancelled navigation
    // for exactly the users the page is for. AuthGuard is the deliberate
    // "open to any logged-in user" marker shared with calendar, compliances
    // and property-workers. Nothing else would notice PermissionGuard being
    // reinstated — this spec is that guard.
    it('is registered as a child of the plugin root route', () => {
      const route = findRoute(routes, 'adhoc-tasks');

      expect(route).toBeDefined();
      expect(route.loadChildren).toBeDefined();
    });

    it('is guarded by AuthGuard only — open to any logged-in plugin user', () => {
      const route = findRoute(routes, 'adhoc-tasks');

      expect(route.canActivate).toContain(AuthGuard);
    });

    it('does not use PermissionGuard', () => {
      const route = findRoute(routes, 'adhoc-tasks');

      expect(route.canActivate).not.toContain(PermissionGuard);
    });

    it('declares no requiredPermission', () => {
      const route = findRoute(routes, 'adhoc-tasks');

      expect(route.data?.['requiredPermission']).toBeUndefined();
    });
  });

  it('keeps the plugin-wide access gate on the root route', () => {
    // Out of scope for 2026-08-24: opening the parent would change access to
    // every page in the plugin, not just ad-hoc tasks.
    expect(routes[0].canActivate?.length).toBe(1);
    expect(routes[0].data?.['requiredPermission']).toBe(
      'backend_configuration_plugin_access'
    );
  });
});
