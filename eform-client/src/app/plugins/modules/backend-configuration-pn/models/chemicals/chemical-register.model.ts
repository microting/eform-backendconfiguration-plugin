export interface ChemicalHazardStatementModel {
  code: string;
  text: string;
}

export interface ChemicalActiveSubstanceModel {
  name: string;
  casNo: string;
  concentration: string;
}

export interface ChemicalProductModel {
  productId: number;
  name: string;
  barcode: string;
  sdsFileName: string;
  sdsChecksum: string;
}

/** Plugin ChemicalRegisterEntryModel: BMD details, Danish texts unchanged. Dates are ISO strings. */
export interface ChemicalRegisterEntryModel {
  chemicalId: number;
  name: string;
  registrationNo: string;
  status: number | null;
  statusText: string;
  salesDeadline: string | null;
  useAndPossessionDeadline: string | null;
  authorisationDate: string | null;
  authorisationExpirationDate: string | null;
  authorisationTerminationDate: string | null;
  hazardPictograms: string[];
  signalWord: number | null;
  signalWordText: string;
  hazardStatements: ChemicalHazardStatementModel[];
  activeSubstances: ChemicalActiveSubstanceModel[];
  authorisationHolder: string;
  products: ChemicalProductModel[];
  updatedAt: string;
}

export interface ChemicalRegisterPageModel {
  entries: ChemicalRegisterEntryModel[];
  total: number;
}
