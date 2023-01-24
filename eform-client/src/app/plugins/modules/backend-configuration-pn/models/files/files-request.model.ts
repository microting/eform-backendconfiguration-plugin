import {SortModel} from 'src/app/common/models';

export interface FilesRequestModel extends SortModel {
  propertyIds: number[],
  tagIds: number[],
  dateFrom: string,
  dateTo: string,
  nameFilter: string,
}
