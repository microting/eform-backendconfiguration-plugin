# Design: mobile event attachments

## Context

The flutter-eform mobile app shows two separate surfaces for files on an
event card today:

1. **PhotoGrid** inside the Picture form field — renders photos captured
   by the worker via the mobile camera. Fed by the local Drift
   `opgave_photos` table, populated by the photo-sync stream.
2. **AttachmentRow loop** at the tail of the card — currently shows
   **mobile-captured photos a second time** as numeric rows like `711`,
   because the server's `EventsGrpcService.PopulateAttachments` reads
   `Cases.Custom.OpgaverPhotos[]` and emits each as a proto
   `Attachment { source=UNSPECIFIED, name=<UploadedDataId> }`.

Meanwhile, an admin-side feature exists per
`docs/superpowers/specs/2026-05-06-calendar-event-attachments-design.md`
(deployed): admins can upload files (PDF / PNG / JPEG, ≤25 MB, ≤10 per
planning) to an event via the angular `task-create-edit-modal` "Attach
file" button. These files land in the plugin's `AreaRulePlanningFiles`
table with full metadata (`OriginalFileName`, `MimeType`, `SizeBytes`).
They are NOT exposed to the mobile worker today — `PopulateAttachments`
never queries that table.

User-visible consequence on production right now:
- Event "Em eForm test" (today, 2026-05-19) has one PDF attached via the
  angular admin. Mobile shows **0** rows for that PDF.
- Events with mobile-captured photos show **photo-ID** rows that
  duplicate the PhotoGrid thumbnails.

This design fixes both in one combined spec, delivered as two sequential
PR pairs (hotfix first, feature second).

## Phase 1 — Hotfix: drop the photo→attachment duplicate

Goal: stop the bogus AttachmentRow rendering of mobile-captured photos.

### Server (plugin → `stable`)

**File:** `eFormAPI/Plugins/BackendConfiguration.Pn/BackendConfiguration.Pn/Services/GrpcServices/EventsGrpcService.cs`

Delete the `PopulateAttachments` method (lines ~396-416 today) and all
six call sites that invoke it (lines 283, 372, 1058, 1746, 1948, 3119 —
exact line numbers may drift; locate by symbol). The method has no
non-photo population path; deleting it makes `Event.attachments` empty
on the wire.

No proto change required at this phase — `Attachment` keeps its current
shape.

### Mobile (flutter-eform → `master`)

**File:** `packages/microting_mobile/lib/features/events/data/events_repository.dart`

In `_pbToDomain` (around line 605-612), filter out attachments whose
`source == docpb.AttachmentSource.UNSPECIFIED` before mapping:

```dart
attachments: o.attachments
    .where((a) => a.source != docpb.AttachmentSource.UNSPECIFIED)
    .map((a) => Attachment(
          source: a.source == docpb.AttachmentSource.ONEDRIVE
              ? AttachmentSource.onedrive
              : AttachmentSource.gdrive,
          name: a.name,
        ))
    .toList(),
```

This is defense-in-depth: the server delete is the canonical fix, but
old/yet-to-update plugin servers will keep emitting UNSPECIFIED photo
rows for a while. The filter ignores them on every device that ships
this mobile build.

### Tests

- Mobile: extend `packages/microting_mobile/test/features/events/data/events_repository_write_test.dart` with a test that asserts `_pbToDomain` drops UNSPECIFIED-source attachments while keeping GDRIVE / ONEDRIVE / (Phase 2's CALENDAR_FILE) entries.
- Plugin: existing tests on `EventsGrpcService` should continue to pass; the deleted method has no callers after the delete so no regression test needed.

### Verification (Phase 1)

- Deploy the plugin to `stable` → production. Run the existing mobile
  build pointing at production. Open an event with photos. Confirm zero
  AttachmentRow entries.
- Build new mobile via fastlane with the mapper filter, install on
  emulator. Repeat against any not-yet-redeployed environment. Confirm
  same outcome.

## Phase 2 — Feature: expose web event-attachments on mobile

Goal: render the PDFs the admin uploaded via angular as `AttachmentRow`
entries with the real filename, tappable to open the file via the
device's native viewer.

### Proto changes

**File:** `eFormAPI/Plugins/BackendConfiguration.Pn/BackendConfiguration.Pn/Protos/documents.proto`

Add a new enum value:

```proto
enum AttachmentSource {
  UNSPECIFIED = 0;
  GDRIVE = 1;
  ONEDRIVE = 2;
  CALENDAR_FILE = 3;   // file attached to the event via the angular admin
                       // (AreaRulePlanningFile)
}
```

**File:** `eFormAPI/Plugins/BackendConfiguration.Pn/BackendConfiguration.Pn/Protos/events.proto`

Extend the `Attachment` message:

```proto
message Attachment {
  microting.documents.AttachmentSource source = 1;
  string name = 2;                  // legacy / fallback display label
  int32 id = 3;                     // AreaRulePlanningFile.Id (CALENDAR_FILE only)
  string original_file_name = 4;    // e.g. "Em eForm test.pdf"
  string mime_type = 5;             // e.g. "application/pdf"
  int64 size_bytes = 6;
}
```

Existing `gdrive` / `onedrive` consumers continue to populate only
`source` + `name` and ignore the new fields (proto3 defaults).

Add a new RPC on `EventsService`:

```proto
rpc GetEventAttachment(GetEventAttachmentRequest) returns (GetEventAttachmentResponse);

message GetEventAttachmentRequest {
  int32 event_id = 1;       // AreaRulePlanningId
  int32 attachment_id = 2;  // AreaRulePlanningFile.Id
}

message GetEventAttachmentResponse {
  bytes content = 1;
  string mime_type = 2;
  string original_file_name = 3;
}
```

Files ≤25 MB (per existing spec) fit comfortably in a single response;
no streaming required.

### Server (plugin)

**New helper** in `EventsGrpcService.cs`:

```csharp
private async Task PopulateCalendarAttachments(
    Event proto,
    int areaRulePlanningId,
    BackendConfigurationPnDbContext dbContext,
    CancellationToken ct)
{
    var files = await dbContext.AreaRulePlanningFiles
        .Where(f => f.AreaRulePlanningId == areaRulePlanningId
                 && f.WorkflowState != Constants.WorkflowStates.Removed)
        .OrderBy(f => f.Id)
        .Select(f => new { f.Id, f.OriginalFileName, f.MimeType, f.SizeBytes })
        .ToListAsync(ct)
        .ConfigureAwait(false);

    foreach (var f in files)
    {
        proto.Attachments.Add(new Attachment
        {
            Source = AttachmentSource.CalendarFile,
            Id = f.Id,
            OriginalFileName = f.OriginalFileName ?? "",
            MimeType = f.MimeType ?? "",
            SizeBytes = f.SizeBytes,
            Name = f.OriginalFileName ?? f.Id.ToString(CultureInfo.InvariantCulture),
        });
    }
}
```

Call from the six sites that previously called the (now-deleted)
`PopulateAttachments`: `ListEvents`, `GetEvent`, `StreamEventChanges`,
`CompleteEvent`, `SetComment`, and the bulk one-shot path.

**New RPC handler** `GetEventAttachment(GetEventAttachmentRequest)` —
reuses the existing
`BackendConfigurationCalendarService.DownloadFile(int planningId, int fileId)` plumbing (already proven by the REST endpoint
`GET /api/backend-configuration-pn/calendar/tasks/{id}/files/{fileId}`).
Authorize against the worker's site assignment for the planning (mirror
the REST endpoint's permission check). Return bytes + mime + filename.

### Mobile (flutter-eform)

**Regenerate proto stubs** after the proto edit:
```
cd packages/microting_mobile && bash tool/gen_proto.sh
```

**Domain** (`lib/features/events/domain/attachment.dart`):

```dart
enum AttachmentSource { gdrive, onedrive, calendarFile }

class Attachment {
  const Attachment({
    required this.source,
    required this.name,
    this.id = 0,
    this.originalFileName = '',
    this.mimeType = '',
    this.sizeBytes = 0,
  });
  final AttachmentSource source;
  final String name;
  final int id;
  final String originalFileName;
  final String mimeType;
  final int sizeBytes;
}
```

**Mapper** (`events_repository.dart` `_pbToDomain`):
- Remove the Phase 1 `UNSPECIFIED` filter (it's no longer doing anything once Phase 2's server delete has shipped to all environments). Defense-in-depth value is low after deploy is complete; revisit during Phase 2 review.
- Map `CALENDAR_FILE` to `AttachmentSource.calendarFile` and populate the new fields.

**UI** (`lib/features/events/presentation/widgets/event_card.dart` `AttachmentRow`):

```dart
final label = attachment.originalFileName.isNotEmpty
    ? attachment.originalFileName
    : attachment.name;
// Render `label` instead of `attachment.name` directly.
```

No layout change. Optionally show `_humanSize(attachment.sizeBytes)` as
a small trailing text — defer that polish to a follow-up unless trivial.

**Fetch** (`lib/features/documents/data/documents_repository.dart` +
`document_page.dart`):
- Route `/doc/<eventId>/<source>/<idx>` stays unchanged. `idx` is the per-source index within the event's attachments list (same semantics as gdrive/onedrive today).
- `DocumentsRepository.fetch(eventId, source, idx)` for `source == calendarFile`: load the event's attachments, take the `idx`-th entry whose source is calendarFile, read its `Attachment.id` field, call `EventsService.GetEventAttachment(eventId, attachmentId: attachment.id)`.
- Save returned bytes to temp file. Call `OpenFilex.open(tempPath)` with the returned `mime_type`. Same UX as today's gdrive/onedrive path — only the fetcher under the hood changes.

### Tests

- Mobile: extend `events_repository_write_test.dart` with a test that maps a proto `Attachment { source=CALENDAR_FILE, id=42, original_file_name="report.pdf", ... }` to the domain object correctly.
- Mobile: widget test for `AttachmentRow` rendering `originalFileName` when present.
- Plugin: integration test `GetEventAttachment` returns bytes for a known `AreaRulePlanningFile` and 404 for a non-existent one (mirroring the REST endpoint's existing test coverage).

### Verification (Phase 2)

Live emulator pointing at production after both PRs deploy + new mobile
build via fastlane:

1. Open the "Em eForm test" event on 2026-05-19 (which has 1 attached PDF).
2. Confirm AttachmentRow shows one row with the PDF's `original_file_name` (e.g. `"test.pdf"`), no photo-ID rows.
3. Tap the row → DocumentPage fetches via the new RPC → opens via `OpenFilex.open()`.
4. Open an event with mobile-captured photos but no web attachments → AttachmentRow is empty (PhotoGrid still shows thumbnails).
5. Open an event with mobile-captured photos AND web attachments → AttachmentRow shows only the web attachments; PhotoGrid still shows the photo thumbnails.

## Implementation sequencing

Two PR pairs delivered in this order:

1. **Hotfix pair**
   - Plugin PR vs `stable`: delete `PopulateAttachments` + its six call sites.
   - Flutter PR vs `master`: add the UNSPECIFIED mapper filter + test.
   - Ship both. User redeploys backend + rebuilds mobile. Bug closed.
2. **Feature pair**
   - Plugin PR vs `stable`: proto extensions, `PopulateCalendarAttachments`, `GetEventAttachment` RPC.
   - Flutter PR vs `master`: regenerated stubs, domain/mapper updates, AttachmentRow label change, `DocumentsRepository` fetch path. The Phase 1 UNSPECIFIED filter can stay (defensive) or be removed at this point.

The pairs are independent — Phase 2 doesn't require Phase 1 to ship
first, but landing Phase 1 first keeps the user-visible bug fixed during
Phase 2's longer development window.

## Existing pieces being reused

- `AreaRulePlanningFile` entity + repository — implemented per
  `2026-05-06-calendar-event-attachments-design.md`. Schema, FK,
  workflow-state lifecycle all in place.
- `BackendConfigurationCalendarService.DownloadFile` — already wraps the
  byte-fetch from S3 / disk for the REST endpoint. New gRPC handler
  calls it directly.
- `OpenFilex.open(...)` — flutter-eform already uses it for gdrive /
  onedrive doc viewing.
- Drift `opgave_photos` table + `PhotoGrid` widget — untouched. Photos
  continue to render only via the form-field surface.

## Out of scope

- Mobile-side upload of new files (`task-create-edit-modal` remains the
  only upload UI per the existing spec).
- Per-rotation attachment (existing spec locks scope to
  `AreaRulePlanning` master-rule — all rotations of a recurring event
  share the same files).
- Google Drive integration (deferred per existing spec).
- Filtering by MIME on the mobile (server already enforces pdf/png/jpeg).
- Showing file sizes / dates / uploader names in the AttachmentRow
  (would need additional proto fields + UI work; consider as a polish
  follow-up).

## Risks + mitigations

- **Old mobile clients see new CALENDAR_FILE source as UNSPECIFIED.**
  Once Phase 1 ships, those clients drop the entries — same as the
  current pre-Phase-2 behavior. Acceptable.
- **The Phase 1 mapper filter masks legitimate UNSPECIFIED attachments
  if the server ever introduces non-photo UNSPECIFIED ones.** The
  current `AttachmentSource` enum's `UNSPECIFIED` is only emitted by the
  to-be-deleted `PopulateAttachments`; deleting it removes the only
  producer. Filter is safe.
- **`GetEventAttachment` returns large PDFs in-memory.** Server-side
  cap of 25 MB per file (existing spec) keeps this within a single
  gRPC response. If the cap is raised in future, switch to a streaming
  RPC.
