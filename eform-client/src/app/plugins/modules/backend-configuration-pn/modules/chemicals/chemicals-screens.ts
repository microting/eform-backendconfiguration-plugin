import {Type} from '@angular/core';
import {
  ChemicalHistoryPageComponent,
  ChemicalInventoryPageComponent,
  ChemicalLocationsPageComponent,
  ChemicalPermissionsPageComponent,
  ChemicalSettingsPageComponent,
} from './components';

export interface ChemicalScreen {
  /**
   * Router path relative to the Kemi container: /plugins/backend-configuration-pn/chemicals/{path}.
   * Never '': the host menu marks a link active on every URL it prefixes, so the bare Kemi root
   * would light up on every screen (Task 12 I-4). The root redirects to the first screen.
   */
  path: string;
  /** Page title shown by the container (mockup web-admin.html). */
  titleKey: string;
  /** E2EId of the plugin menu child that links here (EformBackendConfigurationPlugin menu), pinned as a literal. */
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
    path: 'inventory',
    titleKey: 'Chemicals: Inventory title',
    menuE2EId: 'backend-configuration-pn-chemicals-inventory',
    component: ChemicalInventoryPageComponent,
  },
  {
    path: 'locations',
    titleKey: 'Chemicals: Locations title',
    menuE2EId: 'backend-configuration-pn-chemicals-locations',
    component: ChemicalLocationsPageComponent,
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
  {
    path: 'history',
    titleKey: 'Chemicals: History title',
    menuE2EId: 'backend-configuration-pn-chemicals-history',
    component: ChemicalHistoryPageComponent,
  },
];
