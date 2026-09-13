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
    const rootRoute = findRoute(routes, '');

    expect(routes.length).toBe(1);
    expect(rootRoute).toBeDefined();
    expect(rootRoute.children?.length).toBeGreaterThan(0);
  });

  describe('adhoc-tasks route', () => {
    // Ad-hoc tasks are intentionally open to every user of this plugin
    // (spec 2026-08-24-adhoc-tasks-unrestricted-access-design). The
    // adhoc_enable claim was never granted to non-admin roles and is enforced
    // nowhere server-side, so PermissionGuard silently cancelled navigation
    // for exactly the users the page is for. AuthGuard is the deliberate
    // "open to any logged-in user" marker shared with calendar, compliances
    // and property-workers. This spec is the fast guard against
    // PermissionGuard being reinstated; the e2e suite added with the same
    // change (playwright .../z/adhoc-nonadmin-access.spec.ts) would fail
    // too, but only in CI and only end-to-end.
    it('is registered as a child of the plugin root route', () => {
      const route = findRoute(routes, 'adhoc-tasks');

      expect(route).toBeDefined();
      expect(route.loadChildren).toBeDefined();
    });

    it('is guarded by AuthGuard only — open to any logged-in plugin user', () => {
      const route = findRoute(routes, 'adhoc-tasks');

      // Exact array, not `toContain`: `[AuthGuard, IsAdminGuard]` contains
      // AuthGuard too, and IsAdminGuard would re-close the page to exactly
      // the users it was opened for — and it is already imported by this
      // routing table and used by other plugin routes, so it is one word
      // away.
      expect(route.canActivate).toEqual([AuthGuard]);
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

  describe('compliance-report route', () => {
    // The standalone Compliance page (#1160/#1163). Two things this spec
    // pins, both of which are one word away from regressing:
    //
    //  1. It is a sibling of 'compliances', not a child. compliance.routing.ts
    //     declares ':propertyId' first, so a literal segment moved under that
    //     subtree would silently render CompliancesContainerComponent with a
    //     garbage propertyId instead of 404-ing.
    //  2. AuthGuard only. Decision 6 in #1160 opens the page to every
    //     authenticated plugin user; the calendar view mode it replaces is
    //     hidden behind four client-side admin guards, and re-adding
    //     IsAdminGuard/PermissionGuard here would quietly restore that.
    // `findRoute` is a depth-first search over the WHOLE tree, so it proves
    // only that a route with this path exists SOMEWHERE. Placement is the
    // thing under test here, so these two assertions inspect the plugin root's
    // own `children` array directly instead.
    it('is a DIRECT child of the plugin root route', () => {
      const rootRoute = findRoute(routes, '');
      const directChildren = rootRoute.children ?? [];

      const route = directChildren.filter((r) => r.path === 'compliance-report');

      // Exactly one, and at depth 1 — not three levels down, which the
      // recursive lookup would also have accepted.
      expect(route.length).toBe(1);
      expect(route[0].loadChildren).toBeDefined();
    });

    it('is a SIBLING of the compliances route, not nested under it', () => {
      const rootRoute = findRoute(routes, '');
      const directChildren = rootRoute.children ?? [];
      const compliances = directChildren.find((r) => r.path === 'compliances');

      expect(compliances).toBeDefined();
      // The old form of this test searched `compliances.children ?? []`.
      // `compliances` is a `loadChildren` route, so `.children` is ALWAYS
      // undefined and the search ran over an empty array — it could not fail.
      // Assert the two facts that can: `compliances` declares no eager child
      // table at all, and `compliance-report` sits beside it at the same level.
      expect(compliances.children).toBeUndefined();
      expect(directChildren.some((r) => r.path === 'compliance-report')).toBe(true);
    });

    it('is guarded by AuthGuard only — open to any logged-in plugin user', () => {
      const route = findRoute(routes, 'compliance-report');

      // Exact array, not `toContain`: `[AuthGuard, IsAdminGuard]` contains
      // AuthGuard too.
      expect(route.canActivate).toEqual([AuthGuard]);
    });

    it('does not use PermissionGuard', () => {
      const route = findRoute(routes, 'compliance-report');

      expect(route.canActivate).not.toContain(PermissionGuard);
    });

    it('declares no requiredPermission', () => {
      const route = findRoute(routes, 'compliance-report');

      expect(route.data?.['requiredPermission']).toBeUndefined();
    });
  });

  it('keeps the plugin-wide access gate on the root route', () => {
    // Out of scope for 2026-08-24: opening the parent would change access to
    // every page in the plugin, not just ad-hoc tasks.
    const rootRoute = findRoute(routes, '');

    expect(rootRoute).toBeDefined();
    expect(rootRoute.canActivate?.length).toBe(1);
    expect(rootRoute.data?.['requiredPermission']).toBe(
      'backend_configuration_plugin_access'
    );
  });
});
