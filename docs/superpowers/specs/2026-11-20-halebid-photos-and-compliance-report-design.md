# Halebid: photos on outbreaks, segmented Ja|Nej, outbreaks in the compliance report

Issue: microting/eform-backendconfiguration-plugin#1517
Mockups (private canvas, owner's account): https://claude.ai/artifact/QiMiRqqtBk5jMNt4wy1MgR,
the boards titled "Nyt: …". They show the intended look; where this spec is more precise
(multi-pen registrations, cancelled rows, offline), the spec wins.

Owner decisions, 2026-11-20:
- the Halebid section in the compliance report is shown only to Halebid managers of the
  property (option A);
- the CSV carries one row per outbreak; actions and registrations are in the PDF only.

## Goal

A manager looking at an outbreak sees the photos the barn workers took, on the web and in the
app. The risk assessment's Ja|Nej uses the same toggle as the compliance report's
Oversigt|Detaljer|Rapport. The compliance report's Rapport view, and its PDF and CSV export,
gain a section listing the outbreaks of the period, so a farm can document its tail-bite
handling next to its other compliance work.

## What exists (verified 2026-11-20 against plugin stable / base master)

- Outbreak data: `TailBiteOutbreak` (PropertyId, LocationId, RuleVersion, OpenedAt, ClosedAt,
  ClosedBySiteId); registrations through `TailBiteOutbreakLink` → `TailBiteRegistrationLocation`
  (one row per pen: MinorCount, SevereCount) → `TailBiteRegistration` (SiteId, EffectiveAt,
  CancelledAt, ClientUuid); `TailBiteRiskAssessment` (Water, Feed, ActivityMaterial, Climate,
  Health, Management, AssessedAt); `TailBiteAssessmentAction` (Factor, Description,
  ResponsibleSiteId, FollowUpDate, DoneAt, DoneBySiteId, WithdrawnAt). All timestamps are UTC.
  No migration is needed for anything below.
- Photos belong to a REGISTRATION (not a pen): `TailBiteRegistrationPhoto` (PhotoUuid,
  PropertyId, UploadedBySiteId, RegistrationClientUuid, SdkUploadedDataId). The ownership rule
  (`TailBitePhotoOwnership`, TailBiteModels.cs): same client uuid, same property, same uploading
  site, and `SdkUploadedDataId != 0` (stored). Bytes: `TailBitePhotoStorage` →
  `IAdhocPhotoStorage` (S3 or the local adhoc folder, per `s3Enabled`).
- Serving: gRPC `GetPhoto` (worker check; every manager is a worker). It answers NotFound for
  an unknown uuid but PermissionDenied for a foreign one, which tells a caller which uuids exist.
  No REST endpoint; the web registrations DTO (one row per pen) has only `PhotoCount`; the app
  cannot learn an outbreak's photo uuids.
- Access: tail-bite web routes run through `TailBiteRunner`, which returns
  `OperationResult`/`OperationDataResult` (JSON) only. `RequireCallerSiteAsync` throws
  `TailBiteForbiddenException` when the caller has no worker identity or the email maps to
  several workers.
- Compliance report: `[Authorize]` only; `PropertyId` is `int?` (null = all properties); the
  period is local calendar days. Rapport = `POST compliance-report/eform-columns`. Export:
  static `BuildReport` → `ComplianceExportWordWriter` (A4 landscape, filter header, Microting
  footer, images by NAME via `InsertImage`, `{id}_700_{checksum}`) → PDF; the CSV writer
  flattens EVERY table of the document into one union table (only a per-column `CsvOnly` flag
  exists). `MaxAppendixImages` (200) caps images per document.
- Toggle: `.segmented` / `.segmented-option` is global under `body.theme-workspace`
  (eform-angular-frontend `_workspace-utilities.scss`); the Classic-theme copy and the
  `:focus-visible` rule live in `compliance-report-page.component.scss`. Neither theme styles
  `:disabled`.
- App: `LocalDataCleaner` empties the temp dir on an explicit sign-out only. Demo outbreak 31
  (Stald 3 › Sektion 4) has registrations 601 and 602; 32 and 33 have none.

## Common rules (all three PRs)

- Time zone: dates and times shown or exported are Europe/Copenhagen. A report period
  `DateFrom … DateTo` means local days: `[DateFrom 00:00, DateTo+1 00:00)` Copenhagen time,
  converted to UTC before comparing with tail-bite timestamps.
- A photo is shown or counted when it passes the ownership rule AND its registration is not
  cancelled. Every count in this spec uses that rule.
- A registration that covers several pens appears once in photo lists; its place is shown as
  the pens joined with ", " (e.g. "Sti 311, Sti 312").

## Delivery: three PRs

PR 1 needs an eform-angular-frontend PR merged and deployed before the plugin part. PR 2 and
PR 3 are independent of each other (PR 3 reuses PR 2's ownership query if merged first, else
adds it); suggested order 1, 2, 3.

### PR 1 — Ja|Nej as a segmented toggle

eform-angular-frontend (first): move the Classic-theme `.segmented` rules and the
`.segmented-option:focus-visible` rule into the shared SCSS (Classic under `body.theme-eform`),
and add a disabled state for both themes (`.segmented-option:disabled`: no hover, default
cursor, text in `--md-on-surface` at 38 % opacity).

Plugin (after the frontend is deployed):
- Delete the moved rules from `compliance-report-page.component.scss`.
- `tail-bite-assessment-form.component.html`: replace `mat-button-toggle-group` with
  `<div class="segmented" role="radiogroup">` and two `<button type="button"
  class="segmented-option" role="radio">` with `[class.active]`, `[attr.aria-checked]`,
  `[disabled]="readOnly || busy"`. Keep the ids `tailBiteFactor-{factor}-yes|no` and
  `tailBiteFactorRow-{factor}` (the Playwright spec uses them). Drop `MatButtonToggleModule`.
- Three states (unanswered, Ja, Nej). Keyboard as the compliance toggle (arrow keys move and
  select); the checked button has `tabindex=0`, and when unanswered the Ja button does.
- Tests: assessment form spec (answer, change, disabled, keyboard); Playwright tail-bite web
  admin spec still passes.

### PR 2 — Photos on the web outbreak page and in the app

Backend (plugin):
- Hardening, own commit: gRPC `GetPhotoAsync` also requires the property to be enabled, and an
  unknown, unstored, foreign or switched-off photo all answer the same NotFound.
- `OutbreakRegistrations` gains `Photos: [{ uuid, registrationId }]` per registration (not per
  pen row), and `PhotoCount` follows the common rule. On the web, photos show on the
  registration's first row; its other pen rows and cancelled rows show "–".
- New REST `GET api/backend-configuration-pn/tail-bite/photos/{photoUuid}` returning
  `IActionResult` (not via `TailBiteRunner`, which only returns JSON): resolve the caller's site,
  then `RequireEnabledManagerAsync(site, photo.PropertyId)`; `TailBiteNotFoundException` and
  `TailBiteForbiddenException` both map to 404 with no body; success returns the bytes, the
  content type from the extension, and `Content-Disposition: inline`.
- New gRPC `ListOutbreakPhotos(outbreak_id)` → `[{ photo_uuid, registration_id, location_ids,
  effective_at, site_name }]`, newest first; manager of an enabled property (the app shows
  outbreaks to managers only); cancelled registrations excluded.
- Tests (Integration.Test): ownership rule (other site, other property, unstored → excluded),
  cancelled excluded, multi-pen registration listed once, manager / worker / foreign property /
  switched-off property, identical 404 and NotFound answers.

Web (plugin Angular), as the "Nyt: udbrud med fotos og Ja|Nej" board:
- Fotos column: up to three 48 px thumbnails and "+n"; a "Fotos fra udbruddet" gallery card
  (time, pens, who); a viewer dialog with previous/next and a download link.
- Images are fetched as blobs through the Angular service (auth header) and shown via object
  URLs, revoked on destroy; a failed image shows a placeholder tile.
- Tests: component spec for thumbnails, gallery, viewer; Playwright: an outbreak with a photo
  shows a thumbnail and opens the viewer.

App (flutter-halebid), as the "Nyt: udbrud med fotos i appen" board:
- Regenerate the gRPC client; `TailBiteApi.listOutbreakPhotos` and `getPhoto`.
- Assessment and follow-up pages: an outbreak summary card (started, bitten pigs, pens) and a
  horizontal photo strip; tapping opens a full-screen viewer with previous/next.
- Photos are held in memory while the outbreak screens are open; nothing is written to disk.
  Offline, the strip shows "Fotos kan ikke hentes uden net".
- Demo mode: `listOutbreakPhotos('31')` returns two bundled sample images on registration 601;
  other demo outbreaks return none. Extend the demo flow test to see the strip.
- Tests: widget tests for the strip, the viewer, offline and empty states.

### PR 3 — Halebid in the compliance report, PDF and CSV (plugin)

Who sees it:
- Resolve the caller's site. If `RequireCallerSiteAsync` throws `TailBiteForbiddenException`
  (only that exception), the caller has no Halebid access: the section is omitted entirely.
- Properties = `PropertyId` (or all when null) ∩ properties where the caller is a manager and
  Halebid is enabled. Empty set → section omitted entirely (no heading in web or PDF, no CSV
  rows). Non-empty set but no outbreaks in the period → web and PDF show the heading and
  "Ingen udbrud i perioden"; the CSV has no Halebid rows.

Data: new `POST compliance-report/tail-bite` taking `ComplianceReportRequestModel`, returning
`{ outbreaks[], totals }`, each outbreak with nested `actions[]`, `registrations[]` and
`photos[]`.
- Period: an outbreak is in the period when `OpenedAt < periodEndUtc` and
  (`ClosedAt` is null or `ClosedAt >= periodStartUtc`) (common rules). `BoardIds`, `TagIds`,
  `SiteIds` and `Status` do not apply.
- Outbreak: id, location path, rule version, opened, assessed (date or none), closed, bitten
  (minor + severe), severe, the six answers, actions total and done (withdrawn excluded), photo
  count.
- Action: factor, description, responsible name, follow-up date, done date, done-by name
  (withdrawn excluded).
- Registration: effective time, pens, minor, severe, action type names, registered-by name,
  photo count (cancelled excluded).
- Totals: outbreaks in the period, closed in the period, bitten pigs, severe, actions done of
  total. Sentence (da): "{n} udbrud var åbne i perioden, {c} blev lukket. {b} bidte grise,
  heraf {s} alvorlige. {d} af {t} handlinger udført."

Web: in Rapport only, after the existing sections, the "Halebid: udbrud i perioden" card with
the four totals and an expandable row per outbreak (factors, actions, photo thumbnails via the
PR 2 photo endpoint if merged, else counts).

PDF:
- After the eForm sections, on a new page: caption "Halebid", title "Halebid: udbrud i
  perioden", the totals sentence, then three tables in the existing table style: Udbrud
  (ID, Lokation, Regel, Startet, Risikovurderet, Lukket, Bidte grise, Alvorlige, Ja-faktorer,
  Handlinger "d af t udført", Billeder "{n} billeder"); Handlinger fra risikovurderingerne
  (Udbrud, Lokation, Faktor, Handling, Ansvarlig, Frist, Udført, Udført af); Registreringer bag
  udbruddene (Udbrud, Dato, Lokation, Mindre, Alvorlige, Handling, Registreret af, Billeder).
  Closed outbreaks and done actions get the existing done-row tint.
- `ComplianceExportTable` gains `PdfOnly`; the Handlinger and Registreringer tables set it and
  the CSV writer skips such tables.
- Appendix (only with "Inkl. billedbilag"): "Bilag – Halebid", one block per registration that
  has photos, captioned `Udbrud {id} · {pens} · {dd.MM.yyyy HH:mm} · {name}`, two images per row.
  `ComplianceExportImageBlock` gains `TailBitePhotos` (photo uuids); `BuildReport` stays static
  and takes the Halebid model; the Word writer gets `ITailBitePhotoStorage` by constructor and an
  `InsertImage(Stream, …)` overload with the same resizing. The Halebid appendix comes last and
  shares `MaxAppendixImages`; when the budget is used up, its later photos are left out and the
  existing "requested vs embedded" note reports it. Accepted.

CSV: the Udbrud table only, appended to the flat file with `Delrapport = Halebid`, one row per
outbreak. Final header order: today's columns unchanged, then the new ones appended:
`Startet` (Text, `yyyy-MM-dd HH:mm` Copenhagen), `Risikovurderet` (Date), `Lukket` (Date),
`Bidte grise`, `Alvorlige` (Number), `Vand`, `Foder`, `Legemateriale`, `Klima`, `Sundhed`,
`Ledelse` (Water, Feed, ActivityMaterial, Climate, Health, Management as "Ja"/"Nej", empty when
not assessed), `Handlinger`, `Handlinger udført` (Number). On Halebid rows the existing `ID`
column holds the outbreak id (the `Delrapport` cell tells it from a case id), `Ejendom` the
property, `Område` the location path and `Billeder` the photo count. Existing rules hold: `;`,
UTF-8 BOM, CRLF, blank for empty, formula guard.

Localisation: new keys in the plugin's `localization.json` (da, en-US, de) and the Angular
i18n files (daDK, enUS, deDE).

Tests (Integration.Test): period edges in Copenhagen time (opened after the period, closed before
it, open across it, closed on the first day, opened 23:30 local on the last day); access (manager,
worker without manager flag, no identity, ambiguous email, switched-off property, PropertyId
null) with no exception reaching the report; withdrawn actions and cancelled registrations
excluded; CSV header order, Halebid rows and no PdfOnly tables in the CSV; PDF document model
(tables, appendix captions, shared image budget). Playwright: Rapport shows the section for a
manager.

## Out of scope

Photos in Oversigt/Detaljer; Halebid in other report modules; editing from the report; a
separate Halebid CSV file; an on-disk photo cache in the app.

## Risks

- The Halebid appendix is last, so a photo-heavy period can use up the image budget; the
  existing note shows it.
- PR 2's app part needs a new app build and store release; the rest needs backend deploys
  (PR 1: frontend first).
