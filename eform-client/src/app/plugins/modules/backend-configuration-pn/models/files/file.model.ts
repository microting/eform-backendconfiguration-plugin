import {SharedTagModel} from 'src/app/common/models';

export interface FileModel {
  id: number,
  fileName: string,
  properties: number[],
  tags: SharedTagModel[],
}
