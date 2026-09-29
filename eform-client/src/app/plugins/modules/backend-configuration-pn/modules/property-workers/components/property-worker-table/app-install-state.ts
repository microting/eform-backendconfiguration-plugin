import {AppInstallModel} from '../../../../models';

/**
 * Line 1 of a Medarbejdere app cell (#1335):
 * - `installed`      green "Ja"  - the app has reported a version;
 * - `no-access`      red "Nej"   - the worker may not use the app;
 * - `not-registered` grey        - may use it, but nothing reported yet.
 *
 * A reported version wins over the access flag: it is a fact about the device,
 * even if access was taken away later. Missing data is never red.
 */
export type AppInstallState = 'installed' | 'no-access' | 'not-registered';

export function appInstallState(app: AppInstallModel | null | undefined): AppInstallState {
  if (app?.version) {
    return 'installed';
  }
  // No app data at all is missing data too, so grey rather than red.
  if (!app || app.hasAccess) {
    return 'not-registered';
  }
  return 'no-access';
}

/** Translation key of the line-1 label. */
export const APP_INSTALL_STATE_LABEL: Record<AppInstallState, string> = {
  'installed': 'Yes',
  'no-access': 'No',
  'not-registered': 'Not registered',
};

/**
 * Chip class of the line-1 label. `not-registered` uses the plain chip, whose
 * neutral surface-variant fill is the grey; tag-success / tag-error are the
 * shared status tags from eform-angular-frontend.
 */
export const APP_INSTALL_STATE_CLASS: Record<AppInstallState, string> = {
  'installed': 'tag-success',
  'no-access': 'tag-error',
  'not-registered': '',
};
