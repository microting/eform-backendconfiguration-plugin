# PDF archive Indbakke: tenant side

## Context

Tenants get an inbound mail address for their PDF archive (`/plugins/backend-configuration-pn/files`). Users
forward mails with PDF attachments to it. A central Microting service receives the mail and suggests property
and tags for each PDF. It then hands the PDF to the tenant's BackendConfiguration plugin. The PDF waits in a new
**Indbakke** until a manager files it into the archive or rejects it.

This document covers only the tenant side, meaning what this plugin, the base repo, the Angular client and the
flutter-archive app implement, plus the wire contract the central service calls. The central service itself is
specified elsewhere.

Rules from the product decisions:
- **A person always files.** Suggestions are preselected. Nothing reaches the archive automatically.
- **Several properties per document are allowed**, because `PropertyFile` is many-to-many.
- **Suggestions are part of the product.** Tenants have no on/off setting.
- **Web and app use the same tenant endpoints.** A document filed on one disappears from the other.
- **Managers** with the new permission `inbox_enable` see the Indbakke.

## Entities (eform-backendconfiguration-base)

All entities are `PnBase` with version twins.

- **`InboxAddress`:** `Address` (the full address), `TokenHash` (sha256 hex of the token), `Active`,
  `GraceUntil`.
  - There is one active row. A rotated-out row keeps working until `GraceUntil = now + 7 days`.
  - The address is shown to managers, so it is stored in plain text. Only its hash is shared with the central
    service.
- **`InboxSenderRule`:** `Pattern` (an address, or `@domain`), `Kind` (Allow/Block).
- **`InboxDocument`:**
  - Identity and metadata: `HubDocumentId` (unique), `FromAddress`, `Subject` (≤500), `ReceivedAt`, `ReadyBy`,
    `DeliveredAt`, `FileName`, `PageCount`, `SizeBytes`, `SpfResult`, `DkimResult`.
  - `Status`: Preparing / SenderPending / Ready / Failed / Filed / Rejected.
  - Review and filing: `FailureReason`, `ReviewedByMicroting`, `Md5` (storage key `{Md5}.pdf`), `FiledFileId`,
    `FiledAt`, `FiledByUserId`.
- **`InboxSuggestion`:** `InboxDocumentId`, `Kind` (Property/Tag), `TargetId`, `Source`
  (TextMatch/Ai/Reviewer), `Confidence` (0..1), `Evidence` (≤250), `Page`, `Reason`, `Accepted`
  (null until filed).
- **Claim constant:** `BackendConfigurationClaims.EnableInbox = "inbox_enable"`.
- **Unknown-sender policy** (Hold or Refuse) is a plugin setting, `BackendConfigurationSettings:InboxUnknownSenderPolicy`.

## Address

- **Format:** `<customerNo>-<token>@indbakke.microting.dk`. The token is 10 characters of `[a-z2-7]`, generated
  with `RandomNumberGenerator`.
- **Creating or rotating** an address registers its hash with the central service through
  `PUT {hub}/api/tenants/{customerNo}/address` with
  `{ tokenHash, previousTokenHash?, graceUntil?, tenantName }`.

## Wire contract with the central service

**Signature on every request, in both directions:**
- Headers: `Authorization: HMAC-SHA256 <sig>`, `Date` (RFC 1123), `X-Request-Id` (a GUID),
  `X-Customer-No`.
- Canonical string: `METHOD\npath\ncustomerNo\nrequestId\nDate\nsha256hex(body)`.
- `sig = hex(HMAC_SHA256(utf8(tenantKey), utf8(canonical)))`. The tenant key is configured per tenant
  (`InboundMailHub:TenantSigningKey`); the hub URL is `InboundMailHub:HubUrl`.
- Rejections: more than ±2 minutes of skew, a wrong customer number, or a request id seen in the last 10
  minutes.
- **Test vector,** which must pass in the plugin's tests:
  - Inputs: tenant key `c25608a9e5271a2bafd480022de09ea60a11c7970566d3dca57034c662d7c767`, customerNo `4711`,
    `POST`, `/api/backend-configuration-pn/inbox/hub/arrived`, requestId `00000000-0000-0000-0000-000000000001`,
    Date `Sun, 04 Oct 2026 10:00:00 GMT`, body `{"a":1}`.
  - Expected body hash: `015abd7f5cc57a2dd94b7590f04ad8084273905ee33ec5cebeae62276a97f862`
  - Expected signature: `be3548b960e7fd4b4dfdd72518c1f33d5d642a0312a36d5b4d7eb1cf2d833b1a`

**Endpoints the central service calls.** All are `[AllowAnonymous]` with signature verification, under
`api/backend-configuration-pn/inbox/hub/`. JSON is camelCase. Enum values are camelCase names (`property`/`tag`, `textMatch`/`ai`/`reviewer`,
`allowed`/`unknown`/`blocked`) and are parsed case-insensitively.

| Endpoint | Body | Response |
|---|---|---|
| `POST arrived` | `{ hubDocumentId, fromAddress, subject, receivedAt, fileName, sizeBytes, spfResult, dkimResult, readyBy }` | `{ senderVerdict: "allowed" \| "unknown" \| "blocked" }`. Also creates an `InboxDocument` (Preparing, or SenderPending for unknown senders); blocked senders get no row |
| `GET catalog` | n/a | `{ properties: [{ id, name, address }], tags: [{ id, name }] }`, live entries only |
| `POST deliver` | multipart: `metadata` = `{ hubDocumentId, pageCount, reviewedByMicroting, suggestions: [{ kind, targetId, source, confidence, evidence, page, reason }] }`, `file` = PDF | `204`. Stores the PDF and suggestions; Status = Ready. Idempotent on `hubDocumentId` |
| `POST failed` | `{ hubDocumentId, reason }` | `204`. Status = Failed with the Danish reason |

The sender verdict:
- **allowed:** the sender matches a user's login email or an Allow rule, and no Block rule.
- **blocked:** a Block rule matches.
- **unknown:** anything else. Under the Refuse policy it becomes `blocked`.

**Endpoint this plugin calls on the central service:** `POST {hub}/api/tenants/{customerNo}/documents/{hubDocumentId}/sender-decision`
with `{ decision: "approve" | "reject" }`.

## Tenant UI endpoints

All are `[Authorize(Policy = "inbox_enable")]`, under `api/backend-configuration-pn/inbox/`.

- `GET ?status=&search=`: the list. `GET {id}`: details with suggestions. `GET {id}/file`: the PDF.
- `POST {id}/file` with `{ name, propertyIds[], tagIds[] }`.
  - It creates `File`, `PropertyFile`, `FileTags` and `UploadedData` **in one transaction**, after the storage
    upload has succeeded. This goes through a new `FileArchiver`, which today's upload `Create` also uses.
  - It sets `Accepted` on the suggestions and Status = Filed.
- `POST {id}/undo`: within 10 minutes of filing. Soft-deletes the File and sets Status back to Ready.
- `POST {id}/reject`: Status = Rejected.
- `POST {id}/approve-sender`: adds an Allow rule for the sender's address, calls sender-decision approve, and
  leaves Status = Preparing.
- `POST {id}/reject-sender`: optionally adds a Block rule, calls sender-decision reject, and sets
  Status = Rejected.
- `GET settings`, `PUT settings` with `{ unknownSenderPolicy, senderRules[] }`, and `POST settings/rotate-address`.

## Angular

- **Sub-navigation** under Arkiv: **Indbakke | Arkiv | Indstillinger e-mail**, matching the eForm new-design
  handoff (`arkiv-indbakke.html`, `arkiv-indstillinger-email.html`).
- **Indbakke:** a table with the columns PDF/emne, Afsender, Modtaget, Ejendom ✦, Tags ✦, Status and Handling.
  The ✦ columns show confidence dots. A review dialog shows the PDF with the evidence highlighted, property and
  tag chips (preselected when confidence ≥ 0.5), the file name, *Arkivér* and *Afvis dokument*.
- **Indstillinger e-mail:** the address with Kopiér and *Lav ny adresse*, allowed senders, the unknown-sender
  policy, and a fixed paragraph about suggestions and retention.
- **SCSS** goes in `eform-angular-frontend`, reusing existing styles first.

## flutter-archive (later phase)

The app gets the same Indbakke for `inbox_enable` users, over the same endpoints: list, review with evidence,
picker sheet, file, reject, approve sender. It also gets a visual refresh to match flutter-chemistry.

## Testing

- **Integration tests** (`BackendConfiguration.Pn.Integration.Test`):
  - the signature test vector and its rejections;
  - each `hub/*` endpoint, including idempotent deliver;
  - sender verdict rules;
  - filing atomicity and undo;
  - reject and approve sender;
  - address rotation.
- **Playwright:** list, review, file (appears in Arkiv), reject, approve sender, settings. Documents are seeded
  by signed calls to `hub/arrived` and `hub/deliver` with a CI-only key.
