/**
 * The central inbound mail service, as the Indbakke spec needs it — both directions.
 *
 * Inbound (hub → tenant): the hub endpoints under /api/backend-configuration-pn/inbox/hub/ accept a
 * request only when it is signed with the tenant key. These helpers sign exactly as the central
 * service does: canonical = `METHOD\npath\ncustomerNo\nrequestId\nDate\nsha256hex(body)`,
 * `Authorization: HMAC-SHA256 hex(HMAC_SHA256(key, canonical))`.
 *
 * Outbound (tenant → hub): creating or rotating the archive address calls the hub first and changes
 * nothing when the hub does not answer. {@link startHubStub} stands in for the hub on the CI runner host;
 * the app container reaches it through InboundMailHub__HubUrl.
 *
 * Both depend on .github/workflows/dotnet-core-pr.yml / dotnet-core-master.yml, step "Start the newly
 * build Docker container": InboundMailHub__TenantSigningKey must equal {@link CI_INBOX_SIGNING_KEY} and
 * InboundMailHub__HubUrl must point at {@link HUB_STUB_PORT} on host.docker.internal.
 */
import { APIRequestContext, APIResponse, expect } from '@playwright/test';
import { createHash, createHmac, randomUUID } from 'crypto';
import { createServer, IncomingMessage } from 'http';
import DatabaseConfigurationConstants from '../../Constants/DatabaseConfigurationConstants';
import { API_TIMEOUT } from './wait-helpers';

/** Must equal InboundMailHub__TenantSigningKey in the CI workflows. CI-only, signs nothing in production. */
export const CI_INBOX_SIGNING_KEY = 'ci-only-inbox-signing-key-0123456789abcdef';

/** Must equal the port in InboundMailHub__HubUrl in the CI workflows. */
export const HUB_STUB_PORT = 4399;

/** The tenant the CI app is set up as (its database is `<customerNo>_Angular`, SDK setting customerNo). */
export const CUSTOMER_NO = String(DatabaseConfigurationConstants.customerNo);

const BASE = 'http://localhost:4200';
const HUB_PATH = '/api/backend-configuration-pn/inbox/hub';

const sha256Hex = (data: Buffer | string): string => createHash('sha256').update(data).digest('hex');

function signature(method: string, path: string, requestId: string, date: string, body: Buffer): string {
  const canonical = [method, path, CUSTOMER_NO, requestId, date, sha256Hex(body)].join('\n');
  return createHmac('sha256', CI_INBOX_SIGNING_KEY).update(canonical).digest('hex');
}

function signedHeaders(method: string, path: string, body: Buffer, contentType?: string): Record<string, string> {
  const date = new Date().toUTCString(); // RFC 1123, what the verifier parses with "R"
  const requestId = randomUUID();
  const headers: Record<string, string> = {
    Authorization: `HMAC-SHA256 ${signature(method, path, requestId, date, body)}`,
    Date: date,
    'X-Request-Id': requestId,
    'X-Customer-No': CUSTOMER_NO,
  };
  if (contentType) {
    headers['Content-Type'] = contentType;
  }
  return headers;
}

async function signedCall(request: APIRequestContext, method: 'GET' | 'POST', endpoint: string, what: string,
  body = Buffer.alloc(0), contentType?: string): Promise<APIResponse> {
  const path = `${HUB_PATH}/${endpoint}`;
  const res = await request.fetch(BASE + path, {
    method,
    data: method === 'POST' ? body : undefined,
    headers: signedHeaders(method, path, body, contentType),
    timeout: API_TIMEOUT,
  });
  if (!res.ok()) {
    throw new Error(`Signed hub call ${what} (${method} ${path}) failed: HTTP ${res.status()} ${await res.text()}`);
  }
  return res;
}

/** A minimal valid one-page PDF. */
export function tinyPdf(text: string): Buffer {
  const stream = `BT /F1 12 Tf 72 720 Td (${text}) Tj ET`;
  const objects = [
    '<< /Type /Catalog /Pages 2 0 R >>',
    '<< /Type /Pages /Kids [3 0 R] /Count 1 >>',
    '<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>',
    `<< /Length ${stream.length} >>\nstream\n${stream}\nendstream`,
    '<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>',
  ];
  let pdf = '%PDF-1.4\n';
  const offsets: number[] = [];
  objects.forEach((o, i) => {
    offsets.push(pdf.length);
    pdf += `${i + 1} 0 obj\n${o}\nendobj\n`;
  });
  const xref = pdf.length;
  pdf += `xref\n0 ${objects.length + 1}\n0000000000 65535 f \n` +
    offsets.map(o => `${String(o).padStart(10, '0')} 00000 n \n`).join('') +
    `trailer\n<< /Size ${objects.length + 1} /Root 1 0 R >>\nstartxref\n${xref}\n%%EOF`;
  return Buffer.from(pdf, 'latin1');
}

export interface CatalogEntry {
  id: number;
  name: string;
}

/** GET catalog: the live properties and file tags, as the central service sees them. */
export async function fetchCatalog(request: APIRequestContext): Promise<{ properties: CatalogEntry[]; tags: CatalogEntry[] }> {
  const res = await signedCall(request, 'GET', 'catalog', 'catalog');
  return res.json();
}

/** A suggestion the central service attaches to a delivered document. */
export interface Suggestion {
  kind: 'property' | 'tag';
  targetId: number;
}

export interface SeedOptions {
  fromAddress: string;
  fileName: string;
  /** Property and tag ids to suggest; sent with confidence 0.5, the review dialog's preselect threshold. */
  suggestions?: Suggestion[];
}

/** What the tenant answered to "arrived": "allowed", or "blocked" when a Block rule matches (then no row exists). */
export interface Arrival {
  hubDocumentId: string;
  senderVerdict: string;
}

/** POST arrived only, as the central service announces a mail before the PDF is ready. */
export async function announceInboxDocument(request: APIRequestContext, fromAddress: string,
  fileName: string): Promise<Arrival> {
  const hubDocumentId = randomUUID();
  const arrived = Buffer.from(JSON.stringify({
    hubDocumentId,
    fromAddress,
    subject: null,
    receivedAt: new Date().toISOString(),
    fileName,
    sizeBytes: 1000,
    spfResult: 'not-checked',
    dkimResult: 'not-checked',
    readyBy: null,
  }));
  const res = await signedCall(request, 'POST', 'arrived', `arrived for ${fileName}`, arrived, 'application/json');
  const { senderVerdict } = await res.json();
  return { hubDocumentId, senderVerdict };
}

/**
 * POST arrived, then POST deliver. Returns the hub document id. The sender must
 * not be blocked: a blocked sender gets no row, so there would be nothing to seed.
 */
export async function seedInboxDocument(request: APIRequestContext, opts: SeedOptions): Promise<string> {
  const { hubDocumentId, senderVerdict } = await announceInboxDocument(request, opts.fromAddress, opts.fileName);
  expect(senderVerdict, `"arrived" for ${opts.fileName} from ${opts.fromAddress}`).toBe('allowed');
  await deliverInboxDocument(request, hubDocumentId, opts.fileName, opts.suggestions ?? []);
  return hubDocumentId;
}

/** POST deliver (multipart: `metadata` JSON + `file` PDF) for a document announced earlier. */
export async function deliverInboxDocument(request: APIRequestContext, hubDocumentId: string, fileName: string,
  suggestions: Suggestion[] = []): Promise<void> {
  const boundary = `----inbox${hubDocumentId}`;
  const metadata = JSON.stringify({
    hubDocumentId,
    pageCount: 1,
    reviewedByMicroting: false,
    suggestions: suggestions.map(s => ({
      ...s, source: 'textMatch', confidence: 0.5, evidence: 'Nordvej 12', page: 1, reason: 'Adressen står i dokumentet.',
    })),
  });
  const body = Buffer.concat([
    Buffer.from(`--${boundary}\r\nContent-Disposition: form-data; name="metadata"\r\nContent-Type: application/json\r\n\r\n${metadata}\r\n`),
    Buffer.from(`--${boundary}\r\nContent-Disposition: form-data; name="file"; filename="${fileName}"\r\nContent-Type: application/pdf\r\n\r\n`),
    tinyPdf('Adresse: Nordvej 12'),
    Buffer.from(`\r\n--${boundary}--\r\n`),
  ]);
  await signedCall(request, 'POST', 'deliver', `deliver for ${fileName}`, body, `multipart/form-data; boundary=${boundary}`);
}

/** One request the app sent to the stub hub. */
export interface HubStubCall {
  method: string;
  path: string;
  body: any;
  /** The request carried a valid tenant signature over exactly this method, path and body. */
  signatureValid: boolean;
}

export interface HubStub {
  /** Every request received so far, in arrival order. */
  calls: HubStubCall[];
  /** The calls received so far with this method and exact path, in arrival order. */
  callsTo(method: string, path: string): HubStubCall[];
  close(): Promise<void>;
}

/** The call carried a valid tenant signature (and the tenant's customer number). */
export function expectSigned(call: HubStubCall, what: string): void {
  expect(call.signatureValid, `${what} is signed with the tenant key`).toBe(true);
}

function readBody(req: IncomingMessage): Promise<Buffer> {
  return new Promise((resolve, reject) => {
    const chunks: Buffer[] = [];
    req.on('data', c => chunks.push(c));
    req.on('end', () => resolve(Buffer.concat(chunks)));
    req.on('error', reject);
  });
}

/**
 * The central service's tenant API (PUT /api/tenants/{n}/address): records each
 * call, checks its signature, and answers 204 — the hub accepted. A call is recorded before it is
 * answered, so once the app's own response reaches the browser the call is already in `calls`.
 */
export function startHubStub(): Promise<HubStub> {
  const calls: HubStubCall[] = [];
  const server = createServer(async (req, res) => {
    const raw = await readBody(req);
    const signatureHeader = String(req.headers['authorization'] ?? '');
    const expected = signature(req.method ?? '', req.url ?? '', String(req.headers['x-request-id'] ?? ''),
      String(req.headers['date'] ?? ''), raw);
    let body: any = null;
    try {
      body = raw.length ? JSON.parse(raw.toString('utf8')) : null;
    } catch {
      body = raw.toString('utf8');
    }
    calls.push({
      method: req.method ?? '',
      path: req.url ?? '',
      body,
      signatureValid: signatureHeader === `HMAC-SHA256 ${expected}` && req.headers['x-customer-no'] === CUSTOMER_NO,
    });
    res.statusCode = 204;
    res.end();
  });
  return new Promise((resolve, reject) => {
    server.once('error', err => reject(new Error(`The stub hub could not listen on port ${HUB_STUB_PORT}: ${err}`)));
    // All interfaces: the app container connects through the docker host gateway, not loopback.
    server.listen(HUB_STUB_PORT, '0.0.0.0', () => resolve({
      calls,
      callsTo: (method, path) => calls.filter(c => c.method === method && c.path === path),
      // The app's HttpClient keeps its connection alive; without dropping it close() waits for its idle timeout.
      close: () => new Promise<void>(done => {
        server.close(() => done());
        server.closeAllConnections();
      }),
    }));
  });
}

/** What the hub is told for an address: lower-case hex SHA-256 of its lower-cased local token. */
export function addressTokenHash(address: string): string {
  const localPart = address.split('@')[0].split('-').slice(1).join('-').toLowerCase();
  return sha256Hex(localPart);
}
