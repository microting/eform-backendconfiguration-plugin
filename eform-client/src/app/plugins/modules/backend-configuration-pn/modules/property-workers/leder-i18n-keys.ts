/**
 * Every translation key the Leder work adds (Medarbejdere and Arkiv). leder-i18n.spec.ts asserts each has a
 * non-empty value in all 26 locale files (translates.ts registers only 14 of them, so a key can look present and
 * still be missing where a customer reads it). Keys and Danish values are the "Web" rows of the Leder contract.
 * API refusals (LederRequiresLogin, LederLoginChangeRequiresAdmin, WorkerTagsRequireLeder) are NOT here: the plugin
 * localizes them and the web shows its `message` as the toast text.
 */
export const LEDER_I18N_KEYS: string[] = [
  'Leader',
  'Requires web or archive access',
  "Only an administrator can change a leader's login",
  'Only administrators and leaders can do this',
  'The move changes who can read the documents. Only an administrator or a leader can do that.',
  'Save the worker with web or archive access first',
];
