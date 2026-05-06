# Calendar event file attachments (PDF / PNG / JPG)

## Context

The calendar event-edit modal currently has a placeholder paperclip "Vedhæft fil" link and a placeholder "Tilføj Google Drev-fil" link — both commented out and not persisted end-to-end. The user wants the attachment side to actually work: pick multiple files, save them with the recurring rule, see them on every occurrence, download / preview, delete. The Google Drive side is split into a follow-up because the platform has no Drive integration today (a from-scratch OAuth + Picker effort gated on a Google Cloud project).

This spec covers **only the file-upload side** for PDF / PNG / JPG / JPEG.

## Scope decisions (all confirmed by user)

- **Recurring scope:** master-rule. One set of files per `AreaRulePlanning`; all occurrences see the same files.
- **Limits:** ≤ 25 MB per file; ≤ 10 files per `AreaRulePlanning`. MIME ∈ {`application/pdf`, `image/png`, `image/jpeg`}; file extensions accepted: `.pdf`, `.png`, `.jpg`, `.jpeg`.
- **Storage:** reuse the existing `EFormFilesController` pipeline — multipart → temp folder → MD5 checksum → `UploadedData` row in SDK schema → `core.PutFileToStorageSystem()` to S3/Swift. New plugin-side join entity links the SDK-owned `UploadedData` to `AreaRulePlanning`.
- **Preview:** PNG/JPEG render as 80×80 thumbnails inline in the modal (loaded via the `downloadUrl`). PDF shows a filetype icon + filename and opens in a new tab on click.
- **Drive integration:** out of scope; the placeholder link stays disabled with "Kommer snart" tooltip until the follow-up spec ships.

## Architecture (3 layers, 2 repos)

### Layer 1 — Base (`eform-backendconfiguration-base`)

New entity `AreaRulePlanningFile` (and audit version `AreaRulePlanningFileVersion`):

```csharp
public class AreaRulePlanningFile : PnBase
{
    public int AreaRulePlanningId { get; set; }
    public AreaRulePlanning AreaRulePlanning { get; set; }
    public int UploadedDataId { get; set; }   // FK to SDK UploadedData
    [StringLength(255)] public string OriginalFileName { get; set; }
    [StringLength(50)]  public string MimeType { get; set; }
    public long SizeBytes { get; set; }
}
```

Plus nav `AreaRulePlanning.AreaRulePlanningFiles` (`ICollection<AreaRulePlanningFile>`).

EF migration `AddAreaRulePlanningFile` via the existing factory class. Tag the next available version after Layer 1's commit (next free is `v10.0.32` — `v10.0.31` was published by another team).

**Unit tests** (in `Microting.EformBackendConfigurationBase.Tests`):
- Round-trip persist + read back (FK + nav).
- Soft-delete via `WorkflowState=Removed` keeps row, `AreaRulePlanning.AreaRulePlanningFiles` filter (consumer-side).
- Cascade behaviour: deleting an `AreaRulePlanning` is non-destructive (the join row's WorkflowState is updated by the service, not the DB).
- 255-char filename + 50-char MIME boundary tests.

### Layer 2 — Plugin C# (`eform-backendconfiguration-plugin`)

**Bump base NuGet** to whatever Layer 1 publishes (`v10.0.32`).

**Service additions** (`BackendConfigurationCalendarService`):

```csharp
Task<OperationDataResult<CalendarTaskAttachmentDto>> UploadFile(int taskId, IFormFile file);
Task<OperationDataResult<List<CalendarTaskAttachmentDto>>> ListFiles(int taskId);
Task<IActionResult> DownloadFile(int taskId, int fileId);
Task<OperationResult> DeleteFile(int taskId, int fileId);
```

Internals:
- `UploadFile`: validate MIME (`application/pdf|image/png|image/jpeg`); validate extension matches MIME (defence in depth); validate size ≤ 25 MB; validate the planning isn't soft-deleted; count existing non-removed `AreaRulePlanningFile` rows — reject if ≥ 10. Compute MD5 from the input stream. Persist `UploadedData` (SDK pattern) → call `core.PutFileToStorageSystem()` → persist `AreaRulePlanningFile` row (calls `Create` so audit version row is written by `PnBase`). Return DTO.
- `ListFiles`: select non-removed `AreaRulePlanningFile` for the planning, project to DTOs.
- `DownloadFile`: load the join + uploaded-data, fetch binary via `core.GetFileFromS3Storage()`, return `FileStreamResult` with `Content-Disposition: inline; filename="…"` so PDFs/images render in the browser tab.
- `DeleteFile`: load the join row, `Delete()` (sets `WorkflowState=Removed`, audit version row written). Leaves `UploadedData` for audit history.

**Endpoints** in `CalendarController`:
- `POST   /api/backend-configuration-pn/calendar/tasks/{id}/files` — `[FromForm] IFormFile file`, ASP.NET multipart binding. Configure `[RequestSizeLimit(26_214_400)]` (25 MB + headroom) at the action attribute.
- `GET    /api/backend-configuration-pn/calendar/tasks/{id}/files` — list metadata.
- `GET    /api/backend-configuration-pn/calendar/tasks/{id}/files/{fileId}` — download stream.
- `DELETE /api/backend-configuration-pn/calendar/tasks/{id}/files/{fileId}`.

**DTO** `CalendarTaskResponseModel` extended with `attachments` so the modal sees them on edit-mode load:

```csharp
public List<CalendarTaskAttachmentDto> Attachments { get; set; } = new();

public class CalendarTaskAttachmentDto
{
    public int Id { get; set; }
    public string OriginalFileName { get; set; } = "";
    public string MimeType { get; set; } = "";
    public long SizeBytes { get; set; }
    public string DownloadUrl { get; set; } = "";  // /api/.../files/{fileId}
}
```

The 4 ARP→DTO mapper sites (per the prior repeat-fields work) populate the list. Empty for plannings with no attachments.

**Integration tests** (`CalendarAttachmentTests` in `BackendConfiguration.Pn.Integration.Test`):

1. `Upload_PdfPngJpeg_ListReturnsAll` — POST one of each, GET list, assert all 3 present with correct MIME / size / filename.
2. `Upload_RoundTripsBinary` — upload, download, assert returned bytes' MD5 == input MD5.
3. `Upload_RejectsOver25Mb` — 26 MB file → 400 with translation key `File too large`.
4. `Upload_RejectsWrongMime` — `.docx` (`application/vnd.openxmlformats-officedocument.wordprocessingml.document`) → 400 with `File type not allowed`.
5. `Upload_RejectsExtensionMimeMismatch` — file uploaded as `report.png` but MIME `application/pdf` → 400.
6. `Upload_RejectsEleventhFile` — pre-seed 10 files, attempt 11th → 400 with `Attachment limit reached`.
7. `Delete_SoftDeletesJoin_PreservesUploadedData` — delete one, list returns the other two, but raw `UploadedData` count unchanged.
8. `Download_StreamsExactBinary_WithInlineDisposition` — assert `Content-Disposition` header includes original filename.
9. `Files_ScopedToPlanning` — upload to A, list B → empty.

Reuse the existing Testcontainers MariaDB harness; mirror `CalendarRepeatPersistenceTests` bootstrap.

### Layer 3 — Frontend (`eform-angular-frontend` host app)

**Service** `BackendConfigurationPnCalendarFilesService`:

```ts
uploadFile(taskId: number, file: File): Observable<OperationDataResult<CalendarTaskAttachment>>;
listFiles(taskId: number): Observable<OperationDataResult<CalendarTaskAttachment[]>>;
downloadUrl(taskId: number, fileId: number): string;
deleteFile(taskId: number, fileId: number): Observable<OperationResult>;
```

Uploads use `FormData` POST, mirroring the existing `TemplateFilesService.addNewImage` pattern.

**Frontend models**: extend `CalendarTaskModel` with `attachments?: CalendarTaskAttachment[]`. New interface:

```ts
interface CalendarTaskAttachment {
  id: number;
  originalFileName: string;
  mimeType: string;
  sizeBytes: number;
  downloadUrl: string;
}
```

**Modal UI** changes (`task-create-edit-modal.component.{html,ts,scss}`):

- Replace the commented-out Drive-link block with two stacked rows:
  - **Drive link row** (top, disabled placeholder): paperclip-Drive icon + greyed link "Tilføj Google Drev-fil" with `matTooltip="Kommer snart"`. Stays for visual fidelity with the mockup.
  - **Attachments row**: paperclip icon + active link "Vedhæft fil".
- Click "Vedhæft fil" → trigger a hidden `<input #fileInput type="file" accept=".pdf,.png,.jpg,.jpeg,application/pdf,image/png,image/jpeg" multiple>` (both `accept` MIME and extensions for OS-level filtering). On `change`, iterate `files` and POST each one sequentially (parallel uploads can race the 10-file quota check on the server).
- During upload: render a per-file progress chip with filename + spinner. On success replace the chip with a real attachment row.
- Below the link, render `attachments` as a flex-wrap list:
  - PNG/JPEG: 80×80 `<img>` thumbnail with `src` = the `downloadUrl`. `loading="lazy"`. On click, open in new tab.
  - PDF: filetype icon (Material `picture_as_pdf`) + filename truncated to ~32 chars. On click, open `downloadUrl` in new tab.
  - Each row has a download icon (open in new tab) and a trash icon (with `confirm()` dialog).
- **Create-mode gating**: while `!task` (no saved planning yet), the "Vedhæft fil" link is disabled with `matTooltip="Gem først for at vedhæfte filer"`. After first save, the modal switches to edit mode and attachments become editable. (No staging of files pre-save — keeps the API one-way.)

**Tests**:

- Karma unit tests for `BackendConfigurationPnCalendarFilesService` — stub `HttpClient`, assert URL/body shape for each method.
- Playwright e2e (`r/calendar-attachments.spec.ts`) — appended after I1 in the existing serial chain. **J1**: create event → save → reopen → upload `sample.pdf` + `sample.png` + `sample.jpg` (test fixtures) → close → reopen → assert all three rows visible with correct icons/filenames → delete the PDF → reload → assert only the PNG and JPG remain.

Test fixtures: small (~10 KB) sample files committed under `eform-client/playwright/fixtures/calendar-attachments/`.

## Critical files

**Base** — `/home/rene/Documents/workspace/microting/eform-backendconfiguration-base/`:
- `Microting.EformBackendConfigurationBase/Infrastructure/Data/Entities/AreaRulePlanningFile.cs` (NEW)
- `Microting.EformBackendConfigurationBase/Infrastructure/Data/Entities/AreaRulePlanningFileVersion.cs` (NEW)
- `Microting.EformBackendConfigurationBase/Infrastructure/Data/Entities/AreaRulePlanning.cs` (modified — nav property)
- `Migrations/<timestamp>_AddAreaRulePlanningFile.{cs,Designer.cs}` (NEW)
- `Microting.EformBackendConfigurationBase.Tests/AreaRulePlanningFileUTest.cs` (NEW)

**Plugin source** — `/home/rene/Documents/workspace/microting/eform-backendconfiguration-plugin/`:
- `eFormAPI/Plugins/BackendConfiguration.Pn/BackendConfiguration.Pn/BackendConfiguration.Pn.csproj` (bump base NuGet)
- `eFormAPI/Plugins/BackendConfiguration.Pn/BackendConfiguration.Pn/Infrastructure/Models/Calendar/CalendarTaskResponseModel.cs` (Attachments field)
- `eFormAPI/Plugins/BackendConfiguration.Pn/BackendConfiguration.Pn/Infrastructure/Models/Calendar/CalendarTaskAttachmentDto.cs` (NEW)
- `eFormAPI/Plugins/BackendConfiguration.Pn/BackendConfiguration.Pn/Controllers/CalendarController.cs` (4 new endpoints)
- `eFormAPI/Plugins/BackendConfiguration.Pn/BackendConfiguration.Pn/Services/BackendConfigurationCalendarService/BackendConfigurationCalendarService.cs` (4 new service methods + 4 mapper-site additions)
- `eFormAPI/Plugins/BackendConfiguration.Pn/BackendConfiguration.Pn.Integration.Test/CalendarAttachmentTests.cs` (NEW)
- `eFormAPI/Plugins/BackendConfiguration.Pn/BackendConfiguration.Pn.Integration.Test/SQL/420_eform-backend-configuration-plugin.sql` (add `AreaRulePlanningFiles` + `AreaRulePlanningFileVersions` to bootstrap)

**Frontend (host app)** — `/home/rene/Documents/workspace/microting/eform-angular-frontend/eform-client/src/app/plugins/modules/backend-configuration-pn/`:
- `models/calendar/calendar-task.model.ts` (Attachments + interface)
- `services/backend-configuration-pn-calendar-files.service.ts` (NEW)
- `services/index.ts` (export new service)
- `modules/calendar/modals/task-create-edit-modal/task-create-edit-modal.component.{ts,html,scss}` (UI + wiring)

**Plugin tests** — `/home/rene/Documents/workspace/microting/eform-backendconfiguration-plugin/`:
- `eform-client/playwright/e2e/plugins/backend-configuration-pn/r/calendar-attachments.spec.ts` (NEW J1 test)
- `eform-client/playwright/fixtures/calendar-attachments/{sample.pdf,sample.png,sample.jpg}` (NEW)

## Reusable pieces

- **`EFormFilesController` upload pipeline** — temp file + MD5 + `UploadedData` row + `core.PutFileToStorageSystem()` → adapt the same flow inside `BackendConfigurationCalendarService.UploadFile`.
- **`PnBase.Create`/`.Delete`/audit version pattern** — `AreaRulePlanningFile` extends `PnBase` so `Create()` / `Delete()` write version rows automatically (we did this exact pattern for `RepeatWeekdaysCsv` in v10.0.30).
- **`TemplateFilesService` FormData pattern** — frontend mirrors this.
- **Existing 4 mapper sites in `BackendConfigurationCalendarService`** — same locations as the repeat-fields work; just project attachments alongside.
- **`OperationDataResult<T>` / `OperationResult`** — existing platform pattern, all controllers return this.

## Sequencing

1. Layer 1 base → commit → tag (next free version) → push tag → CI green → NuGet published.
2. Layer 2 plugin: bump NuGet ref → endpoints + service + DTO + integration tests → push → CI green.
3. Layer 3 frontend: service + modal UI + Karma + Playwright → sync via `devgetchanges.sh` → push → CI green.

Same workflow as the multi-day-weekly feature shipped this session.

## Verification

- Backend integration suite covers the 9 cases above.
- Karma covers the service.
- Playwright J1 covers the full UI round-trip.
- Manual smoke: open dev server, create event, save, reopen, attach a PDF + PNG + JPG, close, reopen, click thumbnails (open in new tab), delete one, reload, confirm.

## Out of scope

- **Google Drive picker** — split to a separate follow-up spec; placeholder link stays disabled with "Kommer snart" tooltip.
- **Drag-and-drop upload** — file-picker only in v1.
- **Image cropping / rotation in modal** — files saved as-is.
- **Antivirus scanning** — relies on platform's existing pipeline (or none) — same posture as `EFormFilesController`.
- **Per-occurrence overrides** — master-rule scope only, per the design Q1 answer.
- **File renaming after upload** — delete + re-upload.
- **Mobile worker app rendering** — separate ticket; the same endpoints will serve it but the mobile UI is not in scope.
- **Quota across multiple plannings** — limit is per `AreaRulePlanning`, not per-property or per-tenant. Enterprise-wide quotas can be added later if needed.
