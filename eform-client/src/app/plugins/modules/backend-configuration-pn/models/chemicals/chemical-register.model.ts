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

/** Proto numbering of ChemicalLabelModel.kind (never the C# ChemicalDocumentType values). */
export enum ChemicalLabelKind {
  Label = 1,
  MinorUse = 2,
}

/** A BMD label document; fileName is the md5 of the file, opened through label/{fileName}. */
export interface ChemicalLabelModel {
  fileName: string;
  kind: ChemicalLabelKind;
  packaging: string;
  revisionDate: string | null;
  revision: string;
}

/** A per-language safety data sheet; language is ISO 639-1, fileName the md5, opened through sds/{fileName}. */
export interface ChemicalSdsDocumentModel {
  language: string;
  fileName: string;
  revisionDate: string | null;
  productName: string;
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
  /** Absent from a server that predates the label documents. */
  labels?: ChemicalLabelModel[];
  /** Absent from a server that predates the SDS documents: the row then falls back to products[].sdsFileName. */
  sdsDocuments?: ChemicalSdsDocumentModel[];
}

export interface ChemicalRegisterPageModel {
  entries: ChemicalRegisterEntryModel[];
  total: number;
}
