/**
 * Every translation key the Leder work adds (Medarbejdere and Arkiv). leder-i18n.spec.ts asserts each has a
 * non-empty value in all 26 locale files (translates.ts registers only 14 of them, so a key can look present and
 * still be missing where a customer reads it). Keys and Danish values are the "Web" rows of the Leder contract.
 * API refusals (LederLoginChangeRequiresAdmin, WorkerTagsRequireLeder) are NOT here: the plugin localizes them and
 * the web shows its `message` as the toast text.
 */
export const LEDER_I18N_KEYS: string[] = [
  'Leader',
  "Only an administrator can change a leader's login",
  'Only administrators and leaders can do this',
  'The move changes who can read the documents. Only an administrator or a leader can do that.',
];

/** The disabled-toggle hints, gone with the access condition (amendment §5, D-L16). */
export const RETIRED_LEDER_I18N_KEYS: string[] = [
  'Requires web or archive access',
  'Save the worker with web or archive access first',
];
