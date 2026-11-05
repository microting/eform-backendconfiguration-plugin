import {AppInstallModel} from './app-install.model';

export class DeviceUserModel {
  id: number;
  siteId: number;
  siteUid: number;
  siteName: string;
  propertyNames:  string | string[];
  propertyIds: number[];
  userFirstName: string;
  userLastName: string;
  language: string;
  unitId: number;
  fullName: string;
  languageCode: string;
  otpCode: number;
  customerNo: number;
  languageId: number;
  normalId: number;
  isLocked: boolean;
  isBackendUser: boolean;
  hasWorkOrdersAssigned: boolean;
  timeRegistrationEnabled: boolean;
  enableMobileAccess: boolean;
  taskManagementEnabled: boolean;
  archiveEnabled: boolean;
  webAccessEnabled: boolean;
  manufacturer: string;
  model: string;
  osVersion: string;
  version: string;
  // Per-app access + last reported version, filled by index-device-user only (#1335).
  complianceApp?: AppInstallModel;
  adHocApp?: AppInstallModel;
  timeApp?: AppInstallModel;
  archiveApp?: AppInstallModel;
  chemistryApp?: AppInstallModel;
  halebidApp?: AppInstallModel;
  pinCode: string;
  employeeNo: string;
  startMonday: number;
  endMonday: number;
  breakMonday: number;
  startTuesday: number;
  endTuesday: number;
  breakTuesday: number;
  startWednesday: number;
  endWednesday: number;
  breakWednesday: number;
  startThursday: number;
  endThursday: number;
  breakThursday: number;
  startFriday: number;
  endFriday: number;
  breakFriday: number;
  startSaturday: number;
  endSaturday: number;
  breakSaturday: number;
  startSunday: number;
  endSunday: number;
  breakSunday: number;
  workerEmail: string;
  phoneNumber: string;
  resigned: boolean;
  resignedAtDate: Date;
  tags: number[];
  /**
   * Leder (contract "Medarbejdere"). In: null = unchanged, applied by the API only for admins. Out: the stored flag of
   * the worker's login; null when the row resolves to no login (no address or an "invalid" one).
   */
  isLeader: boolean | null;
  /** Out only: the plugin manages the login ("eForm users" / "Kun arkiv"); false for an owner-group login or none. */
  loginIsManaged: boolean;
  /** Out only: the login has the admin role or is in "eForm admins" (a Leder anyway; no toggle). */
  isAdminLogin: boolean;
  /** Out only. The next two are the caller's flags, the same on every row. May grant and revoke Leder (= admin). */
  canSetLeder: boolean;
  /** May change worker groups (admin or Leder, D-L12). */
  canEditWorkerTags: boolean;
}
