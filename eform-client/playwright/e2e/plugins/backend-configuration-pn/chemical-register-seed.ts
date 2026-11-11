/**
 * Seeds BMD products into the customer register copy (`420_chemical-base-plugin`)
 * for the Kemi specs. In production the nightly sync fills it; in CI the plugin
 * migrates it empty at boot, and no API writes it (Plan A: the plugin never writes
 * the register). Columns follow the ChemicalsBase 10.0.20 model snapshot: the
 * JSON-converted collections must hold a JSON array ('[]'), and every PnBase
 * column (CreatedAt … WorkflowState) is NOT NULL.
 *
 * SQL rule (E-15): this helper builds SQL text, so it interpolates ONLY values it
 * has validated first. Every string must match {@link SAFE_SQL_TEXT} (names come
 * from `generateRandmString`, i.e. hex digits and hyphens) and still goes through
 * {@link sqlText}; integers are checked with `Number.isInteger`; dates must be
 * yyyy-MM-dd. Anything else throws before a statement is built.
 */
import { customerDatabase, runMariadbSql } from './db-helpers';

export interface RegisterChemicalSeed {
  name: string;
  registrationNo: string;
  status: number;
  salesDeadline?: string;
  useAndPossessionDeadline?: string;
}

/** The only strings the Kemi specs interpolate into SQL: letters, digits, space, '_' and '-', 1-80 characters. */
export const SAFE_SQL_TEXT = /^[A-Za-z0-9 _-]{1,80}$/;

/** A validated string as a quoted SQL literal (still escaped, as a second line of defence). */
export const sqlText = (value: string): string => {
  if (!SAFE_SQL_TEXT.test(value)) {
    throw new Error(`sqlText: '${value}' is not ${SAFE_SQL_TEXT} - refusing to put it into SQL`);
  }
  return `'${value.replace(/\\/g, '\\\\').replace(/'/g, "''")}'`;
};

/** A validated integer as SQL text. */
export const sqlInt = (value: number): string => {
  if (!Number.isInteger(value)) {
    throw new Error(`sqlInt: '${value}' is not an integer - refusing to put it into SQL`);
  }
  return String(value);
};

const sqlDate = (value?: string): string => {
  if (value === undefined) {
    return 'NULL';
  }
  if (!/^\d{4}-\d{2}-\d{2}$/.test(value)) {
    throw new Error(`seedRegisterChemical: '${value}' is not yyyy-MM-dd`);
  }
  return `'${value}'`;
};

const BASE = "NOW(), NOW(), 'created', 0, 0, 1";
const BASE_COLUMNS = 'CreatedAt, UpdatedAt, WorkflowState, CreatedByUserId, UpdatedByUserId, Version';

/**
 * One chemical with one product ("<name> 1 L"), signal word 2, pictogram GHS07 and
 * hazard statement 1. Returns the new `Chemicals.Id`.
 */
export async function seedRegisterChemical(seed: RegisterChemicalSeed): Promise<number> {
  const sql = [
    `INSERT INTO Addresses (${BASE_COLUMNS}, Country) VALUES (${BASE}, 45)`,
    'SET @address = LAST_INSERT_ID()',
    `INSERT INTO AuthorisationHolders (${BASE_COLUMNS}, AddressId, Name, RemoteId) VALUES (${BASE}, @address, 'E2E Holder A/S', UUID())`,
    'SET @holder = LAST_INSERT_ID()',
    `INSERT INTO Dpds (${BASE_COLUMNS}, RiskPhrases) VALUES (${BASE}, '[]')`,
    'SET @dpd = LAST_INSERT_ID()',
    `INSERT INTO Clps (${BASE_COLUMNS}, HazardPictograms, SignalWord) VALUES (${BASE}, '[7]', 2)`,
    'SET @clp = LAST_INSERT_ID()',
    `INSERT INTO HazardStatements (${BASE_COLUMNS}, CLPId, Statement) VALUES (${BASE}, @clp, 1)`,
    `INSERT INTO ClassificationAndLabelings (${BASE_COLUMNS}, CLPId, DPDId) VALUES (${BASE}, @clp, @dpd)`,
    'SET @cal = LAST_INSERT_ID()',
    `INSERT INTO Chemicals (${BASE_COLUMNS}, Name, RegistrationNo, RemoteId, Status, SalesDeadline, UseAndPossesionDeadline,
       AuthorisationHolderId, ClassificationAndLabelingId, BarcodeValue, BiocidePossibleUse, BiocideProductType,
       BiocideSpecialUse, BiocideUser, PesticidePossibleUse, PesticideProductGroup, Verified)
     VALUES (${BASE}, ${sqlText(seed.name)}, ${sqlText(seed.registrationNo)}, UUID(), ${sqlInt(seed.status)},
       ${sqlDate(seed.salesDeadline)}, ${sqlDate(seed.useAndPossessionDeadline)}, @holder, @cal,
       '[]', '[]', '[]', '[]', '[]', '[]', '[]', 1)`,
    'SET @chemical = LAST_INSERT_ID()',
    `INSERT INTO Products (${BASE_COLUMNS}, ChemicalId, Name, Barcode, FileName, Checksum, IsActive, IsValid, Verified)
     VALUES (${BASE}, @chemical, ${sqlText(`${seed.name} 1 L`)}, NULL, '', '', 1, 1, 1)`,
    'SELECT @chemical',
  ].join(';\n');
  const out = await runMariadbSql(sql, `seed register chemical ${seed.name}`, customerDatabase('chemical-base-plugin'));
  const id = Number(out.trim().split('\n').pop());
  if (!Number.isInteger(id) || id <= 0) {
    throw new Error(`seedRegisterChemical: no id returned for ${seed.name} (got '${out}')`);
  }
  return id;
}

/** The id of the one live property called `name` (for the ?propertyId= deeplink); throws unless exactly one matches. */
export async function propertyIdByName(name: string): Promise<number> {
  const out = await runMariadbSql(
    `SELECT Id FROM Properties WHERE Name = ${sqlText(name)} AND WorkflowState <> 'removed'`,
    `find the id of property ${name}`, customerDatabase('eform-backend-configuration-plugin'));
  const ids = out.trim().split('\n').filter((line) => line !== '').map(Number);
  if (ids.length !== 1 || !Number.isInteger(ids[0]) || ids[0] <= 0) {
    throw new Error(`propertyIdByName: expected one property '${name}', got '${out}'`);
  }
  return ids[0];
}

/** yyyy-MM-dd of today + days (UTC), for deadlines relative to the run date. */
export function isoDateFromToday(days: number): string {
  const d = new Date();
  d.setUTCDate(d.getUTCDate() + days);
  return d.toISOString().substring(0, 10);
}

export interface RegisterDocumentSeed {
  chemicalId: number;
  /** ChemicalDocumentType: 1 = Sds, 2 = Label, 3 = MinorUseInstructions. */
  type: 1 | 2 | 3;
  /** The file name the server serves it under: 32 hex digits. */
  md5: string;
  /** ISO 639-1; labels are Danish, like the tenant sync writes them. */
  language?: string;
  packaging?: string;
  revisionDate?: string;
}

/**
 * One Active ChemicalDocuments row of a seeded chemical, the way the tenant sync writes it (labels Source=Bmd,
 * SDS Source=Supplier). The register serves the md5 as the document's file name; the specs stub the PDF
 * download itself, so nothing is fetched from chemicalbase.
 */
export async function seedRegisterDocument(seed: RegisterDocumentSeed): Promise<void> {
  if (!/^[0-9a-f]{32}$/.test(seed.md5)) {
    throw new Error(`seedRegisterDocument: '${seed.md5}' is not a 32 digit hex md5`);
  }
  const packaging = seed.packaging === undefined ? 'NULL' : sqlText(seed.packaging);
  const language = sqlText(seed.language ?? 'da');
  const source = seed.type === 1 ? 3 : 2;
  const sql =
    `INSERT INTO ChemicalDocuments (${BASE_COLUMNS}, ChemicalId, Type, Source, Status, Language, ContentMd5, ContentSha256, ` +
    'SizeBytes, SourcePackaging, SourceRevisionDate, RetrievedAt, LastSeenAt, StaffLocked) ' +
    `VALUES (${BASE}, ${sqlInt(seed.chemicalId)}, ${sqlInt(seed.type)}, ${source}, 1, ${language}, '${seed.md5}', '', 0, ` +
    `${packaging}, ${sqlDate(seed.revisionDate)}, NOW(), NOW(), 0)`;
  await runMariadbSql(sql, `seed a register document for chemical ${seed.chemicalId}`, customerDatabase('chemical-base-plugin'));
}

/** A PDF response the specs hand to the browser in place of the chemicalbase proxy. */
export const STUB_PDF = Buffer.from('%PDF-1.4\n1 0 obj<<>>endobj\ntrailer<<>>\n%%EOF\n');

/** Sets the UI language of the seeded admin (the user setting the SDS language follows); throws unless one login changed. */
export async function setAdminLocale(locale: string): Promise<void> {
  if (!/^[a-z]{2}(-[A-Z]{2})?$/.test(locale)) {
    throw new Error(`setAdminLocale: '${locale}' is not a locale`);
  }
  const out = await runMariadbSql(
    `UPDATE Users SET Locale = '${locale}' WHERE Email = 'admin@admin.com'; SELECT ROW_COUNT();`,
    `set the admin locale to ${locale}`, customerDatabase('Angular'));
  if (out.trim() !== '1') {
    throw new Error(`setAdminLocale: expected one login to change, got '${out}'`);
  }
}
