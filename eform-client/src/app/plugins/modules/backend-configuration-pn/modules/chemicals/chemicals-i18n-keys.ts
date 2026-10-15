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
  // Task 3 - W4 page title (mockup: "Medarbejdere & rettigheder")
  'Chemicals: Permissions title',
  'Chemicals: Could not load properties',
  // Task 4 - W4 permissions
  'Chemicals: Worker',
  'Chemicals: Flag view',
  'Chemicals: Flag view hint',
  'Chemicals: Flag register',
  'Chemicals: Flag register hint',
  'Chemicals: Flag remove',
  'Chemicals: Flag remove hint',
  'Chemicals: Flag stock',
  'Chemicals: Flag stock hint',
  'Chemicals: Flag manage locations',
  'Chemicals: Flag manage locations hint',
  'Chemicals: Flag admin',
  'Chemicals: Flag admin hint',
  'Chemicals: Given',
  'Chemicals: Implied by Admin',
  'Chemicals: Not saved',
  'Chemicals: Copy from another property',
  'Chemicals: Copy permissions from',
  'Chemicals: Copy hint',
  'Chemicals: Permissions scope note',
  'Chemicals: Push recipients note',
  'Chemicals: Unsaved changes',
  'Chemicals: One unsaved change',
  'Chemicals: Undo',
  'Chemicals: Save permissions',
  'Chemicals: No workers',
  'Chemicals: Could not load permissions',
  'Chemicals: Could not save permissions',
  // Task 4 fix round 1 - unsaved-changes confirm, copy feedback, search
  'Chemicals: Discard unsaved changes?',
  'Chemicals: Changed workers',
  'Chemicals: Discard changes',
  'Chemicals: Keep editing',
  'Chemicals: Copied permissions',
  'Chemicals: Nothing to copy',
  'Chemicals: Could not copy permissions',
  'Chemicals: No workers match',
  // Task 8 - W5 settings
  'Chemicals: Settings title',
  'Chemicals: Stock management',
  'Chemicals: Enabled',
  'Chemicals: Disabled',
  'Chemicals: Stock management hint',
  'Chemicals: Weekly digest',
  'Chemicals: Weekly digest hint',
  'Chemicals: Recipients',
  'Chemicals: Add e-mail',
  'Chemicals: Remove recipient',
  'Chemicals: Invalid e-mail',
  'Chemicals: When we warn',
  'Chemicals: Fixed for all customers',
  'Chemicals: Event',
  'Chemicals: Push',
  'Chemicals: Weekly e-mail',
  'Chemicals: Threshold six months',
  'Chemicals: Threshold one month',
  'Chemicals: Threshold passed',
  'Chemicals: Threshold passed hint',
  'Chemicals: Threshold status changed',
  'Chemicals: Threshold status changed hint',
  'Chemicals: Next digest example',
  'Chemicals: Digest subject',
  'Chemicals: Digest slot hint',
  'Chemicals: Digest nothing to act on',
  'Chemicals: Digest passed',
  'Chemicals: Digest upcoming',
  'Chemicals: Digest deregistered or expired',
  'Chemicals: Digest new registrations',
  'Chemicals: Digest footer',
  'Chemicals: Could not save settings',
  'Chemicals: Discard unsaved settings?',
  'Chemicals: Unsaved settings',
  // Task 8 fix round 1
  'Chemicals: Could not load settings',
  'Chemicals: Yes',
];
