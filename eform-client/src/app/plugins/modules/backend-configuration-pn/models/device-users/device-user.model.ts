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
   * What the "Use 1-minute intervals" checkbox showed when the dialog saved. The
   * C# DeviceUserModel accepts it for wire compatibility but no server path reads
   * it (create hardcodes true). Omitted while the dialog does not know the saved state.
   */
  useOneMinuteIntervals?: boolean;
}
