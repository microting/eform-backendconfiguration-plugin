export interface Columns {
  property: boolean;
  task: boolean;
  tags: boolean;
  workers: boolean;
  start: boolean;
  repeat: boolean;
  deadline: boolean;
  calendar: boolean;
}

export interface ColumnsModel {
  columnName: string;
  isColumnEnabled: boolean;
}
