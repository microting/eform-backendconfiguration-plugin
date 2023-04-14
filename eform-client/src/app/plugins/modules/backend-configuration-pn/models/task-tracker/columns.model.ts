export interface IColumns {
  Property: boolean;
  Task: boolean;
  Tags: boolean;
  Workers: boolean;
  Start: boolean;
  Repeat: boolean;
  Deadline: boolean;
}

export interface IPostColumns {
  columnName: string;
  isColumnEnabled: boolean;
}

export interface IColumnsResponseModel {
  userId: number
  columnName: string
  isColumnEnabled: boolean
  id: number
  createdAt: string
  updatedAt: string
  workflowState: any
  createdByUserId: number
  updatedByUserId: number
  version: number
}
