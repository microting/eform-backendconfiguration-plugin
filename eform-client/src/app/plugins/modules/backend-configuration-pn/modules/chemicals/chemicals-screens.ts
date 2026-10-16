import {Type} from '@angular/core';
import {ChemicalInventoryPageComponent, ChemicalPermissionsPageComponent, ChemicalSettingsPageComponent} from './components';

export interface ChemicalScreen {
  /**
   * Router path relative to the Kemi container: /plugins/backend-configuration-pn/chemicals/{path}.
   * '' is the Kemi root itself (W1, the landing screen and the digest deeplink target).
   */
  path: string;
  /** Page title shown by the container (mockup web-admin.html). */
  titleKey: string;
  /**
   * E2EId of the plugin menu child that links here (EformBackendConfigurationPlugin menu). A literal,
   * not derived from the path: W1's path is '' but its id is ...-chemicals-inventory.
   */
  menuE2EId: string;
  /** The page the route renders. */
  component: Type<unknown>;
}

/**
 * Kemi screens in mockup order (Beholdning, Lokationer, Rettigheder, Indstillinger,
 * Historik). Each screen task inserts its own entry together with its menu child, so
 * a release never links a page that does not exist. Drives the child routes.
 */
export const CHEMICAL_SCREENS: ChemicalScreen[] = [
  {
    path: '',
    titleKey: 'Chemicals: Inventory title',
    menuE2EId: 'backend-configuration-pn-chemicals-inventory',
    component: ChemicalInventoryPageComponent,
  },
  {
    path: 'permissions',
    titleKey: 'Chemicals: Permissions title',
    menuE2EId: 'backend-configuration-pn-chemicals-permissions',
    component: ChemicalPermissionsPageComponent,
  },
  {
    path: 'settings',
    titleKey: 'Chemicals: Settings title',
    menuE2EId: 'backend-configuration-pn-chemicals-settings',
    component: ChemicalSettingsPageComponent,
  },
];
