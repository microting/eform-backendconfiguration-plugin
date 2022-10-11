export class CasesModel {
  total: number;
  items: Array<CaseModel> = [];
}

export class CaseModel {
  id: number;
  date: string;
  planningNumber: string;
  locationCode: string;
  buildYear: string;
  type: string;
  location: string;
  status: number;
  fieldStatus: string;
  comment: string;
  numberOfImages: number;
  sdkCaseId: number;
  sdkeFormId: number;
}
