# Board-Delete Cascade Events Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** When a calendar board is deleted, cascade-delete every event placed on that board (reusing the existing per-event series-delete path), and warn the user with an accurate, count-bearing confirmation dialog.

**Architecture:** Backend `DeleteBoard` enumerates the board's events via `CalendarConfiguration.BoardId` and calls the existing private `DeleteEntireSeries(arpId)` for each, then soft-deletes the board last. A new read-only endpoint returns the board's event count for the dialog. The Angular delete-modal fetches that count and shows a permanent-deletion warning. All deletions stay soft (`WorkflowState='Removed'`).

**Tech Stack:** C# / .NET (EF Core, NUnit + Testcontainers MariaDB), Angular (ngx-translate, Angular Material dialog), Playwright e2e.

**Branch:** `feat/board-delete-cascade-events` (already created off `stable`).

**Spec:** `docs/superpowers/specs/2026-06-11-board-delete-cascade-events-design.md`

---

## Key design decisions baked into this plan

- **Full series delete:** each event is removed via `DeleteEntireSeries(arpId)` — identical to a manual single-event delete.
- **No single transaction:** `DeleteEntireSeries` delegates the `AreaRulePlanning` removal to `taskWizardService.DeleteTask`, which uses its own `DbContext`; a clean transaction cannot span both. Therefore: **abort-on-first-failure, board deleted last** — if any event fails, log it and return the failure with the board still intact (recoverable).
- **Soft delete only** via `PnBase.Delete(dbContext)` — never hard-delete.

## File structure

**Backend** (`eFormAPI/Plugins/BackendConfiguration.Pn/BackendConfiguration.Pn/`):
- Modify `Services/BackendConfigurationCalendarService/BackendConfigurationCalendarService.cs` — cascade in `DeleteBoard`; add `GetBoardEventCount`.
- Modify `Services/BackendConfigurationCalendarService/IBackendConfigurationCalendarService.cs` — declare `GetBoardEventCount`.
- Modify `Controllers/CalendarController.cs` — add `GET boards/{id}/event-count`.
- Create `BackendConfiguration.Pn.Integration.Test/CalendarBoardDeleteCascadeTests.cs` — integration tests.

**Frontend** (`eform-client/src/app/plugins/modules/backend-configuration-pn/`):
- Modify `services/backend-configuration-pn-calendar.service.ts` — add `getBoardEventCount`.
- Modify `modules/calendar/modals/board-delete-modal/board-delete-modal.component.ts` — fetch count.
- Modify `modules/calendar/modals/board-delete-modal/board-delete-modal.component.html` — reworded warning.
- Modify `i18n/enUS.ts` and `i18n/da.ts` — new translation keys.

**E2E** (`eform-client/playwright/e2e/plugins/backend-configuration-pn/`):
- Create `board-delete-cascade.spec.ts` — smoke.

---

## Task 1: Backend — `GetBoardEventCount` service method, interface, controller

**Files:**
- Modify: `eFormAPI/Plugins/BackendConfiguration.Pn/BackendConfiguration.Pn/Services/BackendConfigurationCalendarService/IBackendConfigurationCalendarService.cs:44`
- Modify: `eFormAPI/Plugins/BackendConfiguration.Pn/BackendConfiguration.Pn/Services/BackendConfigurationCalendarService/BackendConfigurationCalendarService.cs` (near `DeleteBoard`, ~line 3219)
- Modify: `eFormAPI/Plugins/BackendConfiguration.Pn/BackendConfiguration.Pn/Controllers/CalendarController.cs:92`
- Test: `eFormAPI/Plugins/BackendConfiguration.Pn/BackendConfiguration.Pn.Integration.Test/CalendarBoardDeleteCascadeTests.cs`

- [ ] **Step 1: Write the failing test (count)**

Create `CalendarBoardDeleteCascadeTests.cs` in the integration test project. Model the seeding on `CalendarCompleteOccurrenceTests.cs`. Start with this file (more tests are added in Task 2):

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Services.BackendConfigurationCalendarService;
using BackendConfiguration.Pn.Services.BackendConfigurationLocalizationService;
using Microsoft.Extensions.Logging.Abstractions;
using Microting.eForm.Infrastructure.Constants;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;
using Microting.eFormBackendConfigurationBase.Infrastructure.Data.Entities;
using Microting.ItemsPlanningBase.Infrastructure.Data.Entities;
using Microting.ItemsPlanningBase.Infrastructure.Enums;
using NSubstitute;
using NUnit.Framework;

namespace BackendConfiguration.Pn.Integration.Test;

[TestFixture]
public class CalendarBoardDeleteCascadeTests : TestBaseSetup
{
    private BackendConfigurationCalendarService _service = null!;
    private IBackendConfigurationTaskWizardService _taskWizardService = null!;

    private BackendConfigurationCalendarService BuildService()
    {
        var userService = Substitute.For<IUserService>();
        userService.UserId.Returns(1);
        var coreHelper = Substitute.For<IEFormCoreService>();
        _taskWizardService = Substitute.For<IBackendConfigurationTaskWizardService>();
        _taskWizardService.DeleteTask(Arg.Any<int>())
            .Returns(Task.FromResult(new OperationResult(true)));

        return new BackendConfigurationCalendarService(
            new BackendConfigurationLocalizationService(), userService,
            BackendConfigurationPnDbContext!, coreHelper, Substitute.For<IEventDeployService>(),
            ItemsPlanningPnDbContext!, _taskWizardService,
            NullLogger<BackendConfigurationCalendarService>.Instance);
    }

    /// <summary>Seeds a board plus <paramref name="eventCount"/> events placed on it.
    /// Returns (boardId, list of AreaRulePlanning ids).</summary>
    private async Task<(int boardId, List<int> arpIds)> SeedBoardWithEvents(int eventCount)
    {
        var area = new Area
        {
            Type = AreaTypesEnum.Type1, ItemPlanningTagId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext!.Areas.AddAsync(area);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var property = new Property
        {
            Name = $"BoardCascadeTest-{Guid.NewGuid()}", ItemPlanningTagId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.Properties.AddAsync(property);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var board = new CalendarBoard
        {
            Name = "Board A", Color = "#112233", PropertyId = property.Id,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.CalendarBoards.AddAsync(board);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var arpIds = new List<int>();
        for (var i = 0; i < eventCount; i++)
        {
            var areaRule = new AreaRule
            {
                AreaId = area.Id, PropertyId = property.Id, EformId = 0,
                WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
            };
            await BackendConfigurationPnDbContext.AreaRules.AddAsync(areaRule);
            await BackendConfigurationPnDbContext.SaveChangesAsync();

            var planning = new Planning
            {
                Enabled = true, RepeatEvery = 1, RepeatType = RepeatType.Week,
                StartDate = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
                RelatedEFormId = 0, WorkflowState = Constants.WorkflowStates.Created,
                CreatedByUserId = 1, UpdatedByUserId = 1
            };
            await ItemsPlanningPnDbContext!.Plannings.AddAsync(planning);
            await ItemsPlanningPnDbContext.SaveChangesAsync();

            var arp = new AreaRulePlanning
            {
                AreaRuleId = areaRule.Id, PropertyId = property.Id, AreaId = area.Id,
                ItemPlanningId = planning.Id,
                StartDate = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
                Status = true, RepeatType = 2, RepeatEvery = 1,
                WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
            };
            await BackendConfigurationPnDbContext.AreaRulePlannings.AddAsync(arp);
            await BackendConfigurationPnDbContext.SaveChangesAsync();

            var calConfig = new CalendarConfiguration
            {
                AreaRulePlanningId = arp.Id, BoardId = board.Id, StartHour = 9.0, Duration = 1.0,
                WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
            };
            await BackendConfigurationPnDbContext.CalendarConfigurations.AddAsync(calConfig);
            await BackendConfigurationPnDbContext.SaveChangesAsync();

            arpIds.Add(arp.Id);
        }

        return (board.Id, arpIds);
    }

    [Test]
    public async Task GetBoardEventCount_ReturnsDistinctEventCount()
    {
        _service = BuildService();
        var (boardId, _) = await SeedBoardWithEvents(3);

        var result = await _service.GetBoardEventCount(boardId);

        Assert.That(result.Success, Is.True);
        Assert.That(result.Model, Is.EqualTo(3));
    }
}
```

- [ ] **Step 2: Run the test to verify it fails to compile**

Run (from repo root):
```bash
dotnet test eFormAPI/Plugins/BackendConfiguration.Pn/BackendConfiguration.Pn.Integration.Test/BackendConfiguration.Pn.Integration.Test.csproj --filter "FullyQualifiedName~CalendarBoardDeleteCascadeTests.GetBoardEventCount"
```
Expected: BUILD FAILS — `'IBackendConfigurationCalendarService' does not contain a definition for 'GetBoardEventCount'`.

- [ ] **Step 3: Declare the method on the interface**

In `IBackendConfigurationCalendarService.cs`, immediately after line 44 (`Task<OperationResult> DeleteBoard(int id);`), add:
```csharp
    Task<OperationDataResult<int>> GetBoardEventCount(int id);
```

- [ ] **Step 4: Implement the service method**

In `BackendConfigurationCalendarService.cs`, immediately after the closing brace of `DeleteBoard` (~line 3219), add:
```csharp
    public async Task<OperationDataResult<int>> GetBoardEventCount(int id)
    {
        try
        {
            var count = await backendConfigurationPnDbContext.CalendarConfigurations
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .Where(x => x.BoardId == id)
                .Select(x => x.AreaRulePlanningId)
                .Distinct()
                .CountAsync();

            return new OperationDataResult<int>(true, count);
        }
        catch (Exception e)
        {
            SentrySdk.CaptureException(e);
            logger.LogError(e, "BackendConfigurationCalendarService.GetBoardEventCount: {Message}", e.Message);
            return new OperationDataResult<int>(false,
                $"{localizationService.GetString("ErrorWhileReadingCalendarBoard")}: {e.Message}");
        }
    }
```
Note: `new OperationDataResult<int>(true, count)` uses the BasePn `(bool success, T model)` overload — the standard GET return shape in this codebase. If the compiler reports an ambiguous/missing overload, use `new OperationDataResult<int>(true, "", count)` instead.

- [ ] **Step 5: Add the controller endpoint**

In `CalendarController.cs`, immediately after the `DeleteBoard` action (line 92), add:
```csharp
    [HttpGet("boards/{id:int}/event-count")]
    public async Task<OperationDataResult<int>> GetBoardEventCount(int id)
    {
        return await _backendConfigurationCalendarService.GetBoardEventCount(id);
    }
```

- [ ] **Step 6: Run the test to verify it passes**

Run:
```bash
dotnet test eFormAPI/Plugins/BackendConfiguration.Pn/BackendConfiguration.Pn.Integration.Test/BackendConfiguration.Pn.Integration.Test.csproj --filter "FullyQualifiedName~CalendarBoardDeleteCascadeTests.GetBoardEventCount"
```
Expected: PASS (1 test). The Testcontainers MariaDB spins up automatically.

- [ ] **Step 7: Commit**

```bash
git add eFormAPI/Plugins/BackendConfiguration.Pn/BackendConfiguration.Pn/Services/BackendConfigurationCalendarService/IBackendConfigurationCalendarService.cs \
        eFormAPI/Plugins/BackendConfiguration.Pn/BackendConfiguration.Pn/Services/BackendConfigurationCalendarService/BackendConfigurationCalendarService.cs \
        eFormAPI/Plugins/BackendConfiguration.Pn/BackendConfiguration.Pn/Controllers/CalendarController.cs \
        eFormAPI/Plugins/BackendConfiguration.Pn/BackendConfiguration.Pn.Integration.Test/CalendarBoardDeleteCascadeTests.cs
git commit -m "feat(calendar): add board event-count endpoint"
```

---

## Task 2: Backend — cascade delete in `DeleteBoard`

**Files:**
- Modify: `eFormAPI/Plugins/BackendConfiguration.Pn/BackendConfiguration.Pn/Services/BackendConfigurationCalendarService/BackendConfigurationCalendarService.cs:3192-3219` (`DeleteBoard`)
- Test: `eFormAPI/Plugins/BackendConfiguration.Pn/BackendConfiguration.Pn.Integration.Test/CalendarBoardDeleteCascadeTests.cs`

- [ ] **Step 1: Write the failing cascade test**

Add these two tests to `CalendarBoardDeleteCascadeTests.cs` (the seeding helper from Task 1 is reused):

```csharp
    [Test]
    public async Task DeleteBoard_CascadeDeletesEvents_AndDelegatesEachAreaRulePlanning()
    {
        _service = BuildService();
        var (boardId, arpIds) = await SeedBoardWithEvents(2);

        var result = await _service.DeleteBoard(boardId);

        Assert.That(result.Success, Is.True);

        // Board itself soft-deleted.
        var board = await BackendConfigurationPnDbContext!.CalendarBoards
            .FirstAsync(x => x.Id == boardId);
        Assert.That(board.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Removed));

        // Every CalendarConfiguration on the board soft-deleted.
        var liveConfigs = await BackendConfigurationPnDbContext.CalendarConfigurations
            .Where(x => x.BoardId == boardId)
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
            .CountAsync();
        Assert.That(liveConfigs, Is.EqualTo(0));

        // AreaRulePlanning removal delegated once per distinct event.
        foreach (var arpId in arpIds)
        {
            await _taskWizardService.Received(1).DeleteTask(arpId);
        }
    }

    [Test]
    public async Task DeleteBoard_DoesNotTouchEventsOnOtherBoards()
    {
        _service = BuildService();
        var (boardId, _) = await SeedBoardWithEvents(1);
        var (otherBoardId, otherArpIds) = await SeedBoardWithEvents(1);

        await _service.DeleteBoard(boardId);

        // The other board and its event are untouched.
        var otherBoard = await BackendConfigurationPnDbContext!.CalendarBoards
            .FirstAsync(x => x.Id == otherBoardId);
        Assert.That(otherBoard.WorkflowState, Is.Not.EqualTo(Constants.WorkflowStates.Removed));

        var otherConfigsLive = await BackendConfigurationPnDbContext.CalendarConfigurations
            .Where(x => x.BoardId == otherBoardId)
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
            .CountAsync();
        Assert.That(otherConfigsLive, Is.EqualTo(1));

        await _taskWizardService.DidNotReceive().DeleteTask(otherArpIds[0]);
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run:
```bash
dotnet test eFormAPI/Plugins/BackendConfiguration.Pn/BackendConfiguration.Pn.Integration.Test/BackendConfiguration.Pn.Integration.Test.csproj --filter "FullyQualifiedName~CalendarBoardDeleteCascadeTests.DeleteBoard"
```
Expected: FAIL — `DeleteBoard_Cascade...` fails on `_taskWizardService.Received(1)` (current `DeleteBoard` never calls it) and on live configs being non-zero.

- [ ] **Step 3: Implement the cascade in `DeleteBoard`**

Replace the body of `DeleteBoard` (lines 3192-3219) with:
```csharp
    public async Task<OperationResult> DeleteBoard(int id)
    {
        try
        {
            var board = await backendConfigurationPnDbContext.CalendarBoards
                .Where(x => x.Id == id)
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .FirstOrDefaultAsync();

            if (board == null)
            {
                return new OperationResult(false,
                    localizationService.GetString("CalendarBoardNotFound"));
            }

            // Cascade: delete every event placed on this board, reusing the exact
            // per-event series-delete path used for manual deletes. Events first,
            // board last, so a mid-way failure leaves the board intact (recoverable).
            var arpIds = await backendConfigurationPnDbContext.CalendarConfigurations
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .Where(x => x.BoardId == id)
                .Select(x => x.AreaRulePlanningId)
                .Distinct()
                .ToListAsync();

            foreach (var arpId in arpIds)
            {
                var seriesResult = await DeleteEntireSeries(arpId);
                if (!seriesResult.Success)
                {
                    logger.LogError(
                        "BackendConfigurationCalendarService.DeleteBoard: aborting; failed to delete event series {ArpId} for board {BoardId}",
                        arpId, id);
                    return seriesResult;
                }
            }

            await board.Delete(backendConfigurationPnDbContext);

            return new OperationResult(true,
                localizationService.GetString("CalendarBoardDeletedSuccessfully"));
        }
        catch (Exception e)
        {
            SentrySdk.CaptureException(e);
            logger.LogError(e, "BackendConfigurationCalendarService.DeleteBoard: {Message}", e.Message);
            return new OperationResult(false,
                $"{localizationService.GetString("ErrorWhileDeletingCalendarBoard")}: {e.Message}");
        }
    }
```

- [ ] **Step 4: Run the tests to verify they pass**

Run:
```bash
dotnet test eFormAPI/Plugins/BackendConfiguration.Pn/BackendConfiguration.Pn.Integration.Test/BackendConfiguration.Pn.Integration.Test.csproj --filter "FullyQualifiedName~CalendarBoardDeleteCascadeTests"
```
Expected: PASS (3 tests: the count test + 2 cascade tests).

- [ ] **Step 5: Commit**

```bash
git add eFormAPI/Plugins/BackendConfiguration.Pn/BackendConfiguration.Pn/Services/BackendConfigurationCalendarService/BackendConfigurationCalendarService.cs \
        eFormAPI/Plugins/BackendConfiguration.Pn/BackendConfiguration.Pn.Integration.Test/CalendarBoardDeleteCascadeTests.cs
git commit -m "feat(calendar): cascade-delete a board's events when the board is deleted"
```

---

## Task 3: Frontend — `getBoardEventCount` service method

**Files:**
- Modify: `eform-client/src/app/plugins/modules/backend-configuration-pn/services/backend-configuration-pn-calendar.service.ts:112`

- [ ] **Step 1: Add the service method**

In `backend-configuration-pn-calendar.service.ts`, immediately after the `deleteBoard` method (lines 112-114), add:
```typescript
  getBoardEventCount(id: number): Observable<OperationDataResult<number>> {
    return this.apiBaseService.get(`${BackendConfigurationPnCalendarMethods.Boards}/${id}/event-count`);
  }
```
`OperationDataResult` is already imported at the top of the file (line 4) — no new import needed.

- [ ] **Step 2: Verify it compiles**

Run (from `eform-client/`):
```bash
npx tsc --noEmit -p tsconfig.json
```
Expected: no new errors referencing `backend-configuration-pn-calendar.service.ts`.

- [ ] **Step 3: Commit**

```bash
git add eform-client/src/app/plugins/modules/backend-configuration-pn/services/backend-configuration-pn-calendar.service.ts
git commit -m "feat(calendar): add getBoardEventCount frontend service method"
```

---

## Task 4: Frontend — modal count + reworded confirmation + translations

**Files:**
- Modify: `eform-client/src/app/plugins/modules/backend-configuration-pn/modules/calendar/modals/board-delete-modal/board-delete-modal.component.ts`
- Modify: `eform-client/src/app/plugins/modules/backend-configuration-pn/modules/calendar/modals/board-delete-modal/board-delete-modal.component.html`
- Modify: `eform-client/src/app/plugins/modules/backend-configuration-pn/i18n/enUS.ts`
- Modify: `eform-client/src/app/plugins/modules/backend-configuration-pn/i18n/da.ts`

- [ ] **Step 1: Fetch the count in the modal component**

Replace the full contents of `board-delete-modal.component.ts` with:
```typescript
import {Component, Inject, OnInit} from '@angular/core';
import {MAT_DIALOG_DATA, MatDialogRef} from '@angular/material/dialog';
import {CalendarBoardModel} from '../../../../models/calendar';
import {BackendConfigurationPnCalendarService} from '../../../../services';

export interface BoardDeleteModalData {
  board: CalendarBoardModel;
}

@Component({
  standalone: false,
  selector: 'app-board-delete-modal',
  templateUrl: './board-delete-modal.component.html',
})
export class BoardDeleteModalComponent implements OnInit {
  eventCount = 0;
  countLoaded = false;

  constructor(
    private dialogRef: MatDialogRef<BoardDeleteModalComponent>,
    @Inject(MAT_DIALOG_DATA) public data: BoardDeleteModalData,
    private calendarService: BackendConfigurationPnCalendarService,
  ) {}

  ngOnInit() {
    this.calendarService.getBoardEventCount(this.data.board.id).subscribe(res => {
      if (res && res.success) {
        this.eventCount = res.model;
      }
      this.countLoaded = true;
    });
  }

  onConfirm() {
    this.calendarService.deleteBoard(this.data.board.id).subscribe(res => {
      if (res && res.success) {
        this.dialogRef.close(true);
      }
    });
  }

  onCancel() {
    this.dialogRef.close(null);
  }
}
```

- [ ] **Step 2: Reword the confirmation template**

Replace the full contents of `board-delete-modal.component.html` with:
```html
<h3 mat-dialog-title>{{ 'Delete board' | translate }}</h3>

<div mat-dialog-content>
  <p>{{ 'Are you sure you want to delete the board' | translate }} <strong>{{ data.board.name }}</strong>?</p>
  <p>
    {{ 'This will permanently delete the board and its {{count}} events. This cannot be undone.' | translate:{count: eventCount} }}
  </p>
</div>

<div mat-dialog-actions class="d-flex flex-row justify-content-end">
  <button class="btn-cancel" (click)="onCancel()">{{ 'Cancel' | translate }}</button>
  <button class="btn-delete" [disabled]="!countLoaded" (click)="onConfirm()">{{ 'Delete' | translate }}</button>
</div>
```
The `translate:{count: eventCount}` parameter is interpolated into the `{{count}}` placeholder by ngx-translate.

- [ ] **Step 3: Add the English translation key**

In `i18n/enUS.ts`, add this entry alongside the other board entries (near `'Slet tavle': 'Delete board',`):
```typescript
  'This will permanently delete the board and its {{count}} events. This cannot be undone.':
    'This will permanently delete the board and its {{count}} events. This cannot be undone.',
```

- [ ] **Step 4: Add the Danish translation key**

In `i18n/da.ts`, add alongside the other board entries:
```typescript
  'This will permanently delete the board and its {{count}} events. This cannot be undone.':
    'Dette sletter tavlen og dens {{count}} begivenheder permanent. Dette kan ikke fortrydes.',
```

- [ ] **Step 5: Verify the frontend builds**

Run (from `eform-client/`):
```bash
npx tsc --noEmit -p tsconfig.json
```
Expected: no new errors referencing the board-delete-modal files.

- [ ] **Step 6: Commit**

```bash
git add eform-client/src/app/plugins/modules/backend-configuration-pn/modules/calendar/modals/board-delete-modal/board-delete-modal.component.ts \
        eform-client/src/app/plugins/modules/backend-configuration-pn/modules/calendar/modals/board-delete-modal/board-delete-modal.component.html \
        eform-client/src/app/plugins/modules/backend-configuration-pn/i18n/enUS.ts \
        eform-client/src/app/plugins/modules/backend-configuration-pn/i18n/da.ts
git commit -m "feat(calendar): warn that board deletion permanently removes its events, with count"
```

---

## Task 5: Playwright smoke — end-to-end board deletion with events

**Files:**
- Create: `eform-client/playwright/e2e/plugins/backend-configuration-pn/board-delete-cascade.spec.ts`
- Possibly create (only if missing): `eform-client/playwright/e2e/Page objects/Login.page.ts`

**Prerequisites (must be running before this task):** eform host backend bound on `:5000`/`:5001` (use the `eform-backend-restart` skill, with the freshly rebuilt plugin DLL from Tasks 1-2), and the Angular client on `http://localhost:4200`. Customer/schema is 420 locally; admin login is the user's email with the `secretpassword123!` literal.

- [ ] **Step 1: Confirm the Page-object login helper exists**

Run (from `eform-client/`):
```bash
ls "playwright/e2e/Page objects/Login.page.ts" && grep -n "login" "playwright/e2e/Page objects/Login.page.ts"
```
- If it exists, note the exact `login()` signature and reuse it in Step 3.
- If it does NOT exist, create `playwright/e2e/Page objects/Login.page.ts`:
```typescript
import { Page } from '@playwright/test';

export class LoginPage {
  constructor(private page: Page) {}

  async login(
    email = 'rm@microting.dk',
    password = 'secretpassword123!',
  ) {
    await this.page.goto('http://localhost:4200/auth');
    await this.page.locator('#userNameOrEmail, input[name="email"]').first().fill(email);
    await this.page.locator('#password, input[type="password"]').first().fill(password);
    await this.page.locator('#login-button, button[type="submit"]').first().click();
    await this.page.waitForURL('**/plugins/**', { timeout: 30000 }).catch(() => {});
  }
}
```

- [ ] **Step 2: Discover exact calendar selectors against the live app**

This is an AI-driven discovery step (per the project's full-automation rule), not a placeholder. Using the Playwright MCP browser tools against the running app:
1. Log in and navigate to `http://localhost:4200/plugins/backend-configuration-pn/calendar`.
2. Snapshot the page; record the selectors for: the property selector, the "create board" control, the board list entry + its delete (trash) button, the event-create popover, and the delete-board dialog (`app-board-delete-modal`) confirm button.
3. Record the selector/flow to create one event on a board.

Capture these selectors; they fill the marked spots in Step 3's spec.

- [ ] **Step 3: Write the smoke spec**

Create `board-delete-cascade.spec.ts`. Fill the `/* selector from Step 2 */` spots with the discovered selectors. The structure and assertions are fixed:
```typescript
import { test, expect } from '@playwright/test';
import { LoginPage } from '../../Page objects/Login.page';

test.describe('Calendar — deleting a board cascades to its events', () => {
  test.beforeEach(async ({ page }) => {
    await new LoginPage(page).login();
    await page.goto('http://localhost:4200/plugins/backend-configuration-pn/calendar');
  });

  test('delete-board dialog shows event count and removes the board + its events', async ({ page }) => {
    test.setTimeout(120000);

    // 1. Select a property (selector from Step 2).
    await page.locator(/* property selector from Step 2 */).click();

    // 2. Create a board (selectors from Step 2).
    const boardName = `Cascade smoke ${Date.now()}`;
    await page.locator(/* create-board control from Step 2 */).click();
    await page.locator(/* board name input from Step 2 */).fill(boardName);
    await page.locator(/* save board from Step 2 */).click();
    const boardRow = page.locator(/* board list entry from Step 2 */).filter({ hasText: boardName });
    await expect(boardRow).toBeVisible();

    // 3. Create one event on that board (flow from Step 2).
    //    ...event-create steps from Step 2...

    // 4. Open the delete-board dialog.
    await boardRow.locator(/* delete/trash button from Step 2 */).click();
    const dialog = page.locator('app-board-delete-modal');
    await expect(dialog).toBeVisible();

    // 5. Dialog warns about permanent deletion and shows the event count (1).
    await expect(dialog).toContainText(/permanently/i);
    await expect(dialog).toContainText('1');

    // 6. Confirm deletion.
    await dialog.locator('.btn-delete').click();
    await expect(dialog).toBeHidden();

    // 7. Board is gone from the list.
    await expect(page.locator(/* board list entry from Step 2 */).filter({ hasText: boardName }))
      .toHaveCount(0);
  });
});
```

- [ ] **Step 4: Run the smoke and verify it passes**

Run (from `eform-client/`, app running):
```bash
npx playwright test playwright/e2e/plugins/backend-configuration-pn/board-delete-cascade.spec.ts
```
Expected: 1 passed. On failure, inspect `playwright-results/` video/screenshots, adjust selectors from Step 2, rerun.

- [ ] **Step 5: Commit**

```bash
git add "eform-client/playwright/e2e/plugins/backend-configuration-pn/board-delete-cascade.spec.ts"
# include Login.page.ts only if you created it in Step 1
git commit -m "test(calendar): playwright smoke for board-delete cascade"
```

---

## Final verification (before opening the PR)

- [ ] **Backend full test run** — confirm nothing else broke:
  ```bash
  dotnet test eFormAPI/Plugins/BackendConfiguration.Pn/BackendConfiguration.Pn.Integration.Test/BackendConfiguration.Pn.Integration.Test.csproj --filter "FullyQualifiedName~Calendar"
  ```
  Expected: all calendar tests pass, including the 3 new ones.

- [ ] **Pre-commit dual gate** — run `code-review` and `code-simplifier` subagents in parallel over the full diff (`git diff stable...HEAD`); address findings before the PR.

- [ ] **Open PR to `stable`** (not `master`). If `gh pr edit` fails, use `gh api -X PATCH repos/.../pulls/N`.
