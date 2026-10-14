export interface ChemicalScreen {
  /** Router path relative to the Kemi container: /plugins/backend-configuration-pn/chemicals/{path}. */
  path: string;
  /** Page title shown by the container (mockup web-admin.html). */
  titleKey: string;
  /** E2EId of the plugin menu child that links here (EformBackendConfigurationPlugin menu). */
  menuE2EId: string;
}

/**
 * Kemi screens in mockup order (Beholdning, Lokationer, Rettigheder, Indstillinger,
 * Historik). Each screen task inserts its own entry together with its menu child, so
 * a release never links a page that does not exist. Drives the child routes.
 */
export const CHEMICAL_SCREENS: ChemicalScreen[] = [
  {
    path: 'permissions',
    titleKey: 'Chemicals: Permissions title',
    menuE2EId: 'backend-configuration-pn-chemicals-permissions',
  },
];
