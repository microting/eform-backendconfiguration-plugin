import {CommonTranslationModel} from 'src/app/common/models';

export class ChemicalCreateModel {
  translationsName: CommonTranslationModel[];
  description: string;
  tagsIds: number[];

  planningNumber: string;
  locationCode: string;
  buildYear: string;
  type: string;
}
