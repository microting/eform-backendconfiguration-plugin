import {CommonDictionaryModel} from 'src/app/common/models';

export class AreaModel {
  id: number;
  name: string;
  type: 1 | 2 | 3 | 4 | 5;
  languages: CommonDictionaryModel[] = [];
}
