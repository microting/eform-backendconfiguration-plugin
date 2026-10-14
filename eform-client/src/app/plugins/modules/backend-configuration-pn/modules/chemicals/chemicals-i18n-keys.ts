/**
 * Every translation key the Kemi screens use. chemicals-i18n.spec.ts asserts
 * each has a non-empty value in all 26 locale files (translates.ts registers
 * only 14 of them, so a key can look present and still be missing where a
 * customer reads it). Kemi-only keys carry the 'Chemicals: ' prefix so they
 * never collide with an existing key that has a different Danish value; the
 * reused keys below were verified present in all 26 files.
 */
export const CHEMICAL_REUSED_I18N_KEYS: string[] = [
  'Cancel', 'Save', 'Search', 'Actions', 'Status', 'Property', 'Close', 'Location', 'Name', 'Description',
];

export const CHEMICAL_I18N_KEYS: string[] = [
  ...CHEMICAL_REUSED_I18N_KEYS,
  // Task 3 - shell
  'Chemicals: Title',
  'Chemicals: No properties',
  'Chemicals: Select a property',
  'Chemicals: Not available yet',
  // Task 4 - W4 page title (mockup: "Medarbejdere & rettigheder")
  'Chemicals: Permissions title',
];
