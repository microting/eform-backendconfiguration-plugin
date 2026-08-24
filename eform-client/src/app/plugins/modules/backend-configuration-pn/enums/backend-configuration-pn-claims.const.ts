export const BackendConfigurationPnClaims = {
  accessBackendConfigurationPlugin: 'backend_configuration_plugin_access',
  // Retained but unenforced (2026-08-24): the adhoc-tasks route no longer
  // guards on THIS claim, and no backend attribute references it either. The
  // permission is still seeded, so the admin-settings toggle exists but has no
  // effect. Kept so the seeded row stays legible; proper removal needs a
  // base-repo migration (see spec 2026-08-24-adhoc-tasks-unrestricted-access).
  //
  // Unenforced does NOT mean ungated: AdhocController carries
  // [Authorize(Policy = BackendConfigurationClaims.AccessBackendConfigurationPlugin)],
  // so the ad-hoc endpoints still require accessBackendConfigurationPlugin
  // below. What was dropped is the per-feature claim, not the plugin boundary.
  enableAdhoc: 'adhoc_enable',
  createProperties: 'properties_create',
  getProperties: 'properties_get',
  editProperties: 'property_edit',
  deleteProperties: 'property_delete',
  enableTaskManagement: 'task_management_enable',
  enableDocumentManagement: 'document_management_enable',
  enableChemicalManagement: 'chemical_management_enable',
  enableTimeRegistration: 'time_registration_enable',
  assignProperties: 'properties_assign',
  enableFilesManagement: 'files_management_enable',
};
