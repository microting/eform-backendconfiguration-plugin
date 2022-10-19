export class ReportPnGenerateModel {
  dateTo: string;
  dateFrom: string;
  tagIds: number[];
  type: string;

  constructor(data?: any) {
    if (data) {
      this.dateTo = data.dateTo;
      this.dateFrom = data.dateFrom;
      this.tagIds = data.tagIds;
    }
  }
}
