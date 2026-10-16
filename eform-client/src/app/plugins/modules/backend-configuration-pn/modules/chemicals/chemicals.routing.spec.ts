jest.mock('./components', () => ({
  ChemicalsContainerComponent: class MockChemicalsContainerComponent {},
  ChemicalInventoryPageComponent: class MockChemicalInventoryPageComponent {},
  ChemicalPermissionsPageComponent: class MockChemicalPermissionsPageComponent {},
  ChemicalSettingsPageComponent: class MockChemicalSettingsPageComponent {},
}));

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

  it('opens W1 Beholdning at the Kemi root itself, with no redirect that could loop (M-3)', () => {
    const children = routes[0].children ?? [];
    const root = children.filter((r) => r.path === '');

    expect(children.some((r) => r.redirectTo !== undefined)).toBe(false);
    expect(root).toHaveLength(1);
    expect(root[0].component?.name).toBe('MockChemicalInventoryPageComponent');
    // Without 'full' the empty path would match the start of /permissions and swallow the other screens.
    expect(root[0].pathMatch).toBe('full');
    expect(CHEMICAL_SCREENS[0].path).toBe('');
  });

  it('lets every other screen keep its own non-empty path', () => {
    const others = (routes[0].children ?? []).filter((r) => r.path !== '');

    expect(others.map((r) => r.path)).toEqual(['permissions', 'settings']);
    expect(others.every((r) => r.pathMatch === undefined)).toBe(true);
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
    // W1 lives at the Kemi root (path ''), so its id is not built from the path (M-3).
    expect(CHEMICAL_SCREENS.map((s) => [s.path, s.menuE2EId])).toEqual([
      ['', 'backend-configuration-pn-chemicals-inventory'],
      ['permissions', 'backend-configuration-pn-chemicals-permissions'],
      ['settings', 'backend-configuration-pn-chemicals-settings'],
    ]);
  });
});
