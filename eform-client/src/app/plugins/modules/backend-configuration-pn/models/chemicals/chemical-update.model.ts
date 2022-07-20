import {CommonTranslationModel} from 'src/app/common/models';

export class ChemicalUpdateModel {
  id: number;
  name: string;
  registrationNo: string;

  authorisationDate: string;
  authorisationExpirationDate: string;
  authorisationTerminationDate: string;
  salesDeadline: string;
  useAndPossesionDeadline: string;
  possessionDeadline: string;
  status: number;
  verified: boolean;
  barcode: string;
  fileName: string;
  productName: string;
  productId: number;

}
