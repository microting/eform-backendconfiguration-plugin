import {CommonDictionaryModel} from 'src/app/common/models';

export class PropertyModel {
  id: number;
  name: string;
  chr: string;
  address: string;
  languages: CommonDictionaryModel[] = [];
}
