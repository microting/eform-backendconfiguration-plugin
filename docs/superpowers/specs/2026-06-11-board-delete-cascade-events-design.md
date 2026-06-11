# Cascade-delete a board's events on board deletion

**Date:** 2026-06-11
**Area:** BackendConfiguration plugin — Calendar (`/plugins/backend-configuration-pn/calendar`)
**Repo:** `eform-backendconfiguration-plugin` (backend + `eform-client` frontend in same repo)

## Problem

Deleting a calendar board (`CalendarBoard`) currently soft-deletes only the board
record. The events placed on that board are left untouched in the database, orphaned
under a board that no longer exists. The delete-confirmation dialog reinforces this by
promising the events will merely "lose their board association" rather than be deleted.

The desired behavior: **when a board is deleted, all events associated with that board
are deleted too.**

## Domain model (as found)

- **Board** = `CalendarBoard` (extends `PnBase`, soft-delete via `WorkflowState`).
- **Event** = `AreaRulePlanning` (a core BackendConfiguration planning; can also drive
  item plannings / compliance tasks elsewhere in the plugin).
- **Link** = `CalendarConfiguration`: `AreaRulePlanningId` (FK → event) +
  nullable `BoardId` (soft reference → board, **no FK constraint**, so EF does not
  auto-cascade) + per-placement `StartHour` / `Duration` / `Color`.
- Existing per-event deletion: private `DeleteEntireSeries(int arpId)` in
  `BackendConfigurationCalendarService` — soft-deletes the `CalendarConfiguration` +
  `CalendarOccurrenceExceptions` and delegates the `AreaRulePlanning` deletion to
  `taskWizardService.DeleteTask(arpId)`.
- Existing "events for a board" query pattern: `CalendarConfigurations`
  filtered by `BoardId`, `Select(AreaRulePlanningId).Distinct()`.

## Decisions

1. **Delete depth: full series delete.** For each event on the board, run the exact same
   logic as a manual single-event delete in the calendar (`DeleteEntireSeries`). The
   event is removed everywhere, consistent with manual deletion. (Not "calendar placement
   only".)
2. **Confirmation dialog: warn + show count.** Reword the dialog to state events will be
   permanently deleted and show how many. Backed by a **dedicated count endpoint**.
3. **Deliverables include an automated Playwright smoke** of the end-to-end flow, not just
   a backend service test.

## Design

### 1. Backend — cascade in `DeleteBoard`

File: `eFormAPI/Plugins/BackendConfiguration.Pn/BackendConfiguration.Pn/Services/BackendConfigurationCalendarService/BackendConfigurationCalendarService.cs`
(`DeleteBoard(int id)`, ~line 3192).

Before soft-deleting the board:

1. Enumerate the board's events:
   ```csharp
   var arpIds = await backendConfigurationPnDbContext.CalendarConfigurations
       .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
       .Where(x => x.BoardId == id)
       .Select(x => x.AreaRulePlanningId)
       .Distinct()
       .ToListAsync();
   ```
2. For each `arpId`, call the existing private `DeleteEntireSeries(arpId)` (same-class
   reuse — no new deletion semantics).
3. Then soft-delete the `CalendarBoard` via `board.Delete(dbContext)` as today.

**Ordering:** delete events first, board last. A mid-way failure then leaves the board
intact and recoverable rather than orphaning events under a deleted board.

**All deletions stay soft** (`WorkflowState='Removed'`) per the no-hard-delete rule.

**Transactionality (resolve in planning):** `DeleteEntireSeries` delegates to
`taskWizardService.DeleteTask`, which may use its own `DbContext`. Verify during planning
whether a single DB transaction can cleanly wrap the whole operation. If it can, wrap it.
If it cannot span both contexts, fall back to ordered best-effort with per-event logging
so a partial failure is diagnosable and the board still exists.

### 2. Backend — event-count endpoint

- New endpoint: `GET api/backend-configuration-pn/calendar/boards/{id}/event-count`.
- Controller: `CalendarController` — thin delegate to a new service method
  `GetBoardEventCount(int id)`.
- Service: returns the distinct `AreaRulePlanningId` count for non-removed
  `CalendarConfiguration`s with `BoardId == id` (same query shape as the cascade
  enumeration, `.CountAsync()` on the distinct set).
- Kept as a dedicated endpoint (not folded into `getBoards`) to avoid a per-board count
  query on the main board listing.

### 3. Frontend — count + reworded confirmation

Files under `eform-client/src/app/plugins/modules/backend-configuration-pn/`:

- `services/backend-configuration-pn-calendar.service.ts`: add a `EventCount` method
  constant and `getBoardEventCount(id): Observable<OperationDataResult<number>>`.
- `modules/calendar/modals/board-delete-modal/board-delete-modal.component.ts`: on open,
  fetch the event count for `data.board.id` and expose it to the template.
- `board-delete-modal.component.html`: replace the "lose their board association" line
  with a permanent-deletion warning including the count, e.g.
  *"This will permanently delete the board **{name}** and its **{N}** events. This cannot
  be undone."* Add matching translation keys (English + existing translated languages as
  the repo convention requires).

### 4. Testing

- **Backend service test:** create a board with N events (+ at least one event on a
  *different* board), call `DeleteBoard`, assert:
  - the board is `Removed`,
  - all its `CalendarConfiguration`s and their `AreaRulePlanning`s are `Removed`,
  - events on the other board are untouched.
  Also assert `GetBoardEventCount` returns N before deletion.
- **Playwright smoke** (automated, AI-driven end to end): on `localhost:4200` calendar —
  select a property, create a board, add a couple of events to it, open the delete-board
  dialog, assert the dialog shows the correct count and permanent-deletion wording,
  confirm, and assert the events disappear from the calendar.

## Out of scope

- Adding a real FK constraint between `CalendarConfiguration.BoardId` and `CalendarBoard`
  (schema migration; not required for the cascade and broader than this change).
- Any change to manual single-event deletion behavior.
- Undo / soft-delete recovery UI.
