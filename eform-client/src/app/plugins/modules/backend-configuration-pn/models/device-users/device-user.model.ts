export class DeviceUserModel {
  id: number;
  siteId: number;
  siteUid: number;
  siteName: string;
  propertyNames: string;
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
  taskManagementEnabled: boolean;
}
