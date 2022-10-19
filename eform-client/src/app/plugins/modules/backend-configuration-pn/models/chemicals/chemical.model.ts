import {CommonTranslationModel, SharedTagModel} from 'src/app/common/models';

export class ChemicalModel {
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
  locations: string;
  propertyName: string;
  expiredState: string;
  expiredDate: string;
}

export class ChemicalAssignedSitesModel {
  siteId: number;
  name: string;
  siteUId: number;
}
