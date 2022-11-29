export class ReportEformItemModel {
  id: number;
  microtingSdkCaseDoneAt: string;
  serverTime: string;
  microtingSdkCaseId: number;
  eFormId: number;
  doneBy: string;
  itemName: string;
  postsCount: number;
  imagesCount: number;
  caseFields: { key: number, value: string; }[] = [];
  propertyName: string;
}
