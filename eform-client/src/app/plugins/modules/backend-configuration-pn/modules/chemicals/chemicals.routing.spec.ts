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

  it('gives every screen a unique path and menu E2EId that matches the plugin menu', () => {
    expect(new Set(CHEMICAL_SCREENS.map((s) => s.path)).size).toBe(CHEMICAL_SCREENS.length);
    expect(CHEMICAL_SCREENS.map((s) => s.menuE2EId))
      .toEqual(CHEMICAL_SCREENS.map((s) => `backend-configuration-pn-chemicals-${s.path}`));
    expect(CHEMICAL_SCREENS.every((s) => s.titleKey.startsWith('Chemicals: '))).toBe(true);
  });
});
