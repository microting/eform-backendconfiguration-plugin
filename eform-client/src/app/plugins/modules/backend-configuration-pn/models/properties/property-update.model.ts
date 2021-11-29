import { CommonDictionaryModel } from 'src/app/common/models';

export class PropertyUpdateModel {
  id: number;
  name: string;
  chr: string;
  cvr: string;
  address: string;
  languagesIds: number[] = [];
}
