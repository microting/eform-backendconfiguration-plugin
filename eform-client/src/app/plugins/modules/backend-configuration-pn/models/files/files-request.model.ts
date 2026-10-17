import {SortModel} from 'src/app/common/models';
import {DocumentDateState} from '../archive-folders/archive-folder.models';

export interface FilesRequestModel extends SortModel {
  propertyIds: number[],
  tagIds: number[],
  dateFrom: string,
  dateTo: string,
  nameFilter: string,
  /** Documents directly in this folder; null = all folders (search, "Dato skal tjekkes"). */
  folderId?: number | null,
  /** Suggested = "Dato skal tjekkes". */
  dateState?: DocumentDateState | null,
}
