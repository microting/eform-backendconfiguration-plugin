import {SharedTagModel} from 'src/app/common/models';
import {ServerDate} from '../../modules/tail-bite/shared/tail-bite-dates';
import {ArchiveNamedItem, DocumentDateState} from '../archive-folders/archive-folder.models';

export interface FilesModel {
  id: number,
  createDate: Date,
  fileName: string,
  fileExtension: string,
  properties: string[],
  tags: SharedTagModel[],
  folderId: number,
  /** ["Drift", "Fakturaer"] — shown under the name when a search spans all folders. */
  folderPath: string[],
  /**
   * The server sends "yyyy-MM-ddT00:00:00"; the host's DateInterceptor turns it into a Date (midnight UTC).
   * Read the calendar day with `serverDay` (tail-bite-dates), never with string slicing.
   */
  documentDate: ServerDate | null,
  documentDateState: DocumentDateState,
  /** The EFFECTIVE groups: own ones when `ownAccess`, else the folder's. Empty = everyone with archive access. */
  readGroups: ArchiveNamedItem[],
  ownAccess: boolean,
}
