jest.mock('./components', () => ({
  ChemicalsContainerComponent: class MockChemicalsContainerComponent {},
  ChemicalInventoryPageComponent: class MockChemicalInventoryPageComponent {},
  ChemicalPermissionsPageComponent: class MockChemicalPermissionsPageComponent {},
  ChemicalSettingsPageComponent: class MockChemicalSettingsPageComponent {},
}));

import {TestBed} from '@angular/core/testing';
import {Router, provideRouter} from '@angular/router';
import {routes} from './chemicals.routing';
import {CHEMICAL_SCREENS} from './chemicals-screens';
import {chemicalUnsavedChangesGuard} from './chemicals-unsaved-changes';

describe('ChemicalsRouting', () => {
  it('hangs every screen under the container', () => {
    expect(routes).toHaveLength(1);
    expect(routes[0].path).toBe('');
    expect(routes[0].component?.name).toBe('MockChemicalsContainerComponent');
  });

  it('has one child route per registered screen, in registry order', () => {
    const children = (routes[0].children ?? []).filter((r) => !r.redirectTo);

    expect(children.map((r) => r.path)).toEqual(CHEMICAL_SCREENS.map((s) => s.path));
  });

  it('carries the registry title key on every screen route', () => {
    const children = (routes[0].children ?? []).filter((r) => !r.redirectTo);

    expect(children.map((r) => r.data?.['titleKey'])).toEqual(CHEMICAL_SCREENS.map((s) => s.titleKey));
  });

  it('guards every screen against dropping unsaved changes (I-3)', () => {
    const children = (routes[0].children ?? []).filter((r) => !r.redirectTo);

    expect(children.every((r) => (r.canDeactivate ?? []).includes(chemicalUnsavedChangesGuard))).toBe(true);
  });

  it('sends the bare Kemi root to W1 Beholdning with one relative redirect that cannot loop (M-3, I-4)', () => {
    const redirects = (routes[0].children ?? []).filter((r) => r.redirectTo !== undefined);

    expect(redirects).toEqual([{path: '', pathMatch: 'full', redirectTo: 'inventory'}]);
    expect(CHEMICAL_SCREENS[0].path).toBe('inventory');
    // Every screen has its own non-empty path, so a menu link is active on its own page only.
    expect(CHEMICAL_SCREENS.every((s) => s.path !== '')).toBe(true);
  });

  describe('with the real router', () => {
    let router: Router;

    beforeEach(() => {
      TestBed.configureTestingModule({providers: [provideRouter([{path: 'chemicals', children: routes}])]});
      router = TestBed.inject(Router);
    });

    it('lands the Plan D digest link on W1 and keeps its query parameters', async () => {
      await router.navigateByUrl('/chemicals?propertyId=3&filter=requires-action');

      expect(router.url).toBe('/chemicals/inventory?propertyId=3&filter=requires-action');
    });

    it('leaves a screen path alone', async () => {
      await router.navigateByUrl('/chemicals/permissions?propertyId=3');

      expect(router.url).toBe('/chemicals/permissions?propertyId=3');
    });
  });

  it('gives every screen a component', () => {
    const children = (routes[0].children ?? []).filter((r) => !r.redirectTo);

    expect(CHEMICAL_SCREENS.every((s) => !!s.component)).toBe(true);
    expect(children.every((r) => !!r.component)).toBe(true);
  });

  it('gives every screen a unique path and a Kemi title key', () => {
    expect(new Set(CHEMICAL_SCREENS.map((s) => s.path)).size).toBe(CHEMICAL_SCREENS.length);
    expect(CHEMICAL_SCREENS.every((s) => s.titleKey.startsWith('Chemicals: '))).toBe(true);
  });

  it('pins the menu E2EIds to the literals the C# menu definition and ChemicalsMenuTests use', () => {
    // EformBackendConfigurationPlugin.ChemicalsMenuChild builds
    // `backend-configuration-pn-chemicals-{path}`; jest cannot read the C#, so the
    // literals are pinned on both sides. Extend this list with each screen's menu child.
    expect(CHEMICAL_SCREENS.map((s) => [s.path, s.menuE2EId])).toEqual([
      ['inventory', 'backend-configuration-pn-chemicals-inventory'],
      ['permissions', 'backend-configuration-pn-chemicals-permissions'],
      ['settings', 'backend-configuration-pn-chemicals-settings'],
    ]);
  });
});
