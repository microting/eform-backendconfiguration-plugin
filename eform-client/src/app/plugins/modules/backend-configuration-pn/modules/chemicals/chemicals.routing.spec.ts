jest.mock('./components', () => ({
  ChemicalsContainerComponent: class MockChemicalsContainerComponent {},
  ChemicalPermissionsPageComponent: class MockChemicalPermissionsPageComponent {},
}));

import {routes} from './chemicals.routing';
import {CHEMICAL_SCREENS} from './chemicals-screens';

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

  it('redirects the empty path to the first screen', () => {
    const redirect = (routes[0].children ?? []).find((r) => r.redirectTo);

    expect(redirect?.path).toBe('');
    expect(redirect?.redirectTo).toBe(CHEMICAL_SCREENS[0].path);
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
      ['permissions', 'backend-configuration-pn-chemicals-permissions'],
    ]);
  });
});
