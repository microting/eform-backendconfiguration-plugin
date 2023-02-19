import {SharedTagModel} from 'src/app/common/models';

export interface FilesModel {
  id: number,
  createDate: Date,
  fileName: string,
  fileExtension: string,
  properties: string[],
  tags: SharedTagModel[],
}
