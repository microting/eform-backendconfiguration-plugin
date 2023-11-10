export class ReportPnGenerateModel {
  dateTo: string;
  dateFrom: string;
  tagIds: number[];
  type: string;
  version2: boolean;

  constructor(data?: any) {
    if (data) {
      this.dateTo = data.dateTo;
      this.dateFrom = data.dateFrom;
      this.tagIds = data.tagIds;
      this.version2 = data.version2;
    }
  }
}
