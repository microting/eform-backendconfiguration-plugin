/**
 * One worker's use of one mobile app, as IndexDeviceUser reports it (#1335).
 * `hasAccess` is the permission, not the install; `version` is null until the
 * app has reported one.
 */
export interface AppInstallModel {
  hasAccess: boolean;
  version: string | null;
  model: string | null;
  manufacturer: string | null;
  osVersion: string | null;
}
