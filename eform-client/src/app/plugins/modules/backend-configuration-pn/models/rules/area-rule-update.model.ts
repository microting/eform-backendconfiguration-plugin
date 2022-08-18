import { CommonDictionaryModel } from 'src/app/common/models';
import { AreaRuleTypeSpecificFields } from '../';

export class AreaRuleUpdateModel {
  id: number;
  eformId: number;
  eformName: string;
  translatedNames: CommonDictionaryModel[] = [];
  typeSpecificFields: AreaRuleTypeSpecificFields;
  planningStatus: boolean;
}
