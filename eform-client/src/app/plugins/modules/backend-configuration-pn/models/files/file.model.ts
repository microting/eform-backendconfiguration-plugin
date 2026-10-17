import {SharedTagModel} from 'src/app/common/models';
import {ServerDate} from '../../modules/tail-bite/shared/tail-bite-dates';
import {ArchiveNamedItem, DocumentDateState} from '../archive-folders/archive-folder.models';

export interface FileModel {
  id: number,
  fileName: string,
  properties: number[],
  tags: SharedTagModel[],
  folderId: number,
  folderPath: string[],
  /** See FilesModel.documentDate / serverDay. */
  documentDate: ServerDate | null,
  documentDateState: DocumentDateState,
  /** The EFFECTIVE groups: own ones when `ownAccess`, else the folder's. */
  readGroups: ArchiveNamedItem[],
  ownAccess: boolean,
}
