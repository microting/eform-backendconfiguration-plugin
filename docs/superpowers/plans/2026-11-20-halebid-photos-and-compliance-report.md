# Halebid photos, segmented Ja|Nej and outbreaks in the compliance report — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Managers see the barn workers' photos on an outbreak (web and app), the risk assessment's Ja|Nej uses the
compliance report's segmented toggle, and the compliance report's Rapport view, PDF and CSV list the period's outbreaks.

**Architecture:** Five pull requests in three repositories, in order: (A) eform-angular-frontend moves the segmented
control's Classic rules into shared SCSS; (B) plugin PR 1 swaps the assessment form's toggle; (C) plugin PR 2 adds one
photo query (`ITailBitePhotoQuery`) used by a REST photo endpoint, the outbreak registrations DTO and a new gRPC
`ListOutbreakPhotos`, plus the web gallery; (D) flutter-halebid shows the photos; (E) plugin PR 3 adds
`ITailBiteComplianceReportService`, the `compliance-report/tail-bite` endpoint, the Rapport section and the PDF/CSV
export. No database migration.

**Tech Stack:** C# / ASP.NET Core / EF Core (MariaDB) / gRPC, NUnit integration tests (Testcontainers, CI only);
Angular standalone components, vitest specs, Playwright; Flutter (bloc, grpc, alchemist goldens).

**Spec:** `docs/superpowers/specs/2026-11-20-halebid-photos-and-compliance-report-design.md` (this repo, branch
`feat/1517-halebid-photos-report`). The spec wins over this plan where they disagree.

## Global Constraints

- Tests run ONLY in CI. Never run `dotnet test`, `npm test`, `npx playwright` or `flutter test` locally. `dotnet build`
  and `flutter analyze` may run locally; Angular has no local `node_modules` in the worktrees, so it is verified in CI.
- Every commit passes the dual review gate first (code-review + code-simplifier subagents in parallel), then
  `python3 /home/rene/.claude/hooks/record_review.py --repo . --reviewers code-review,code-simplifier --findings N --disposition "..."`.
  Stage files BY NAME; never `git add .` / `git commit -a`. Never commit to `stable`/`master`.
- No real people's data: fictional names only (Jane Doe, Mette Holm, Jonas Skov, Eksempelgården I/S).
- Time zone for everything shown or exported: `Europe/Copenhagen`. A report period `DateFrom … DateTo` is
  `[DateFrom 00:00, DateTo+1 00:00)` Copenhagen time, converted to UTC before comparing with tail-bite timestamps (all UTC).
- A photo is shown or counted only when it passes `TailBitePhotoOwnership` (same client uuid, property and uploading
  site, `SdkUploadedDataId != 0`), is not `Removed`, AND its registration is not cancelled.
- A registration covering several pens appears ONCE in photo lists; its place is the pens joined `", "`.
- Every user-facing string goes through localisation: plugin `Resources/localization.json` (da, en-US, de) for the
  export, the Angular `i18n/da.ts`, `enUS.ts`, `deDE.ts` for the web (check both `'Key':` and `Key:` forms before adding),
  `lib/l10n/app_{da,en,de}.arb` for the app.
- The id ranges and DOM ids the Playwright spec uses stay: `tailBiteFactor-{factor}-yes|no`, `tailBiteFactorRow-{factor}`.
- Copy: sentence case, no all-caps labels.
- Repositories and target branches: eform-angular-frontend → `stable`; eform-backendconfiguration-plugin → `stable`;
  flutter-halebid → `stable`.

## Rulings made while planning (spec gaps)

1. `ComplianceExportColumn` gains `PdfOnly` as well as `ComplianceExportTable.PdfOnly`: the Halebid Udbrud table's PDF and
   CSV columns differ (spec lists both sets), so one table carries both with per-column flags. The CSV writer skips
   `PdfOnly` tables and columns; the Word writer already skips `CsvOnly` columns.
2. `ComplianceExportTable` gains `Lead` (a paragraph printed under the title: the totals sentence or "Ingen udbrud i
   perioden") and `StartsOnNewPage`. A table with no columns prints only its headings.
3. `ComplianceExportImageBlock.TailBitePhotos` carries SDK `UploadedData` ids (what `ITailBitePhotoStorage.OpenAsync`
   takes), not photo uuids — avoids a second lookup in the writer. The per-block cap is the existing
   `MaxAppendixImagesPerCase` (4), applied per registration.
4. The REST photo endpoint applies the common rule too: a photo whose registration is cancelled answers 404.
5. A Halebid outbreak's location path is the location's ancestors below the root plus itself, joined `" › "`.
6. The PDF "Ja-faktorer" cell: the yes factors joined `", "`; "Mangler vurdering" when not assessed; "Ingen" when assessed
   with no yes.
7. The Halebid Udbrud table carries separate CSV-only and PDF-only columns where the two layouts differ (`Area` "Område"
   CSV-only vs `TailBiteLocation` "Lokation" PDF-only; `Images` CSV-only in today's position vs `TailBitePhotos` PDF-only
   last; empty CSV-only `DoneBy`/`CompletedDate`), so a Halebid-only CSV starts with today's columns too.
9. Photo records: ONE type, `RegistrationPhoto(Guid PhotoUuid, int RegistrationId, [Newtonsoft JsonIgnore] int UploadedDataId)`,
   is used by the web DTO, the report model and the export (the host serialises with Newtonsoft, so the attribute is
   `Newtonsoft.Json.JsonIgnore`).
10. `GrpcSiteResolver` throws `RpcException(Unauthenticated)` for an inactive or resigned account. Every REST caller of
   `RequireCallerSiteAsync` added here treats it like `TailBiteForbiddenException` (section omitted / 404).
8. Demo sample photos are two generated, neutral JPEGs (no text, no people) under `assets/demo/`.

## Review Focus

1. An outbreak opened 23:30 Copenhagen time on the period's last day (22:30 UTC in winter) belongs to the period — Task 8.
2. A registration whose photos were uploaded by ANOTHER worker with the same client uuid is not shown — Task 3.
3. A photo of a cancelled registration never appears: not in the gallery, the app strip, the counts, the PDF appendix or
   via the REST endpoint — Tasks 3, 4, 8, 10.
4. A caller who is a worker but not a manager, or whose email maps to two workers, gets the compliance report WITHOUT the
   Halebid section and without an error — Task 8.
5. The eForm part of an existing CSV export is byte-identical when there is no Halebid section — Task 11.

---

## Repository A — eform-angular-frontend

### Task 1: Shared segmented control (Classic + focus + disabled)

**Worktree:** `git -C /home/rene/Documents/workspace/microting/eform-angular-frontend fetch origin && git -C … worktree add <scratchpad>/wt-fe-1517 -b feat/segmented-shared origin/stable`
(the main checkout is in dev mode; never edit it).

**Files:**
- Create: `eform-client/src/scss/components/_segmented.scss`
- Modify: `eform-client/src/scss/components/_index.scss` (add `@use "segmented";` after `"workspace-utilities"`)

**Interfaces:** Produces the global classes `.segmented`, `.segmented-option` (with `.active`, `:focus-visible`,
`:disabled`) for BOTH themes. Consumed by Tasks 2 and 9 in the plugin.

**Status:** done — microting/eform-angular-frontend#8231. The final selectors are `body .segmented-option:disabled, body .segment:disabled` with `&.active, &[aria-selected="true"], &[aria-pressed="true"]` (they tie with both themes' rules and win by cascade order).

- [ ] **Step 1: Write the partial**

```scss
// _segmented.scss
//
// The segmented control (`.segmented` / `.segmented-option`) in BOTH themes.
// Workspace draws the control itself in _workspace-utilities.scss §5; this adds
// what both themes share — the focus ring and the disabled state — and the
// Classic (theme-eform) drawing, which the compliance report used to carry in
// its component SCSS. Tokens only: every value is a theme custom property.

.segmented-option:focus-visible {
  outline: 2px solid var(--md-primary);
  outline-offset: 1px;
}

// A disabled option (read-only form, request in flight): no hover, default
// cursor, text at 38 % — the Material disabled-content opacity.
.segmented-option:disabled {
  cursor: default;
  color: color-mix(in srgb, var(--md-on-surface) 38%, transparent);

  &.active {
    background: color-mix(in srgb, var(--md-on-surface) 12%, transparent);
  }
}

body.theme-eform {
  .segmented {
    display: inline-flex;
    gap: var(--spacing-xs);
    padding: var(--spacing-xs);
    border: 1px solid var(--md-outline);
    border-radius: var(--radius-sm);
  }

  .segmented-option {
    padding: var(--spacing-sm) var(--spacing-lg);
    border: 0;
    border-radius: var(--radius-sm);
    background: transparent;
    color: var(--md-on-surface);
    font: inherit;
    cursor: pointer;

    &.active {
      background: var(--md-primary-container);
      color: var(--md-on-surface);
    }
  }
}
```

- [ ] **Step 2: Register it** — in `_index.scss` add `@use "segmented";` directly after `@use "workspace-utilities";`.
  The disabled rule must come after §5's `.active` rule in the cascade; check `body.theme-workspace .segmented-option.active`
  (specificity 0,3,1) does not beat `.segmented-option:disabled.active` (0,3,0). If it does, scope the disabled rule as
  `body .segmented-option:disabled` (0,2,1 → raise to `body.theme-workspace .segmented-option:disabled, body.theme-eform
  .segmented-option:disabled`). Write the final selector so a disabled active option is visibly greyed in both themes.
- [ ] **Step 3: Verify** — no local build. Grep that no other SCSS in `eform-client/src` defines `.segmented-option:disabled`.
- [ ] **Step 4: Review gate, commit, PR** — `feat(scss): shared segmented control for both themes` →
  PR to `stable`, watch CI, consider Copilot, merge on green (Task 2 needs it merged first).

---

## Repository B — eform-backendconfiguration-plugin, PR 1 (`feat/1517-segmented-ja-nej` off `stable`)

### Task 2: Assessment form uses the segmented toggle

**Files:**
- Modify: `eform-client/src/app/plugins/modules/backend-configuration-pn/modules/tail-bite/components/tail-bite-assessment-form/tail-bite-assessment-form.component.html`
- Modify: `…/tail-bite-assessment-form/tail-bite-assessment-form.component.ts`
- Modify: `…/tail-bite-assessment-form/tail-bite-assessment-form.component.spec.ts`
- Modify: `…/modules/compliance-report/components/compliance-report-page/compliance-report-page.component.scss` (delete the
  `.segmented-option:focus-visible` rule and the whole `:host-context(body.theme-eform)` block; keep the comment that the
  control is shared, now pointing at `_segmented.scss`)
- Check: `eform-client/playwright/e2e/plugins/backend-configuration-pn/za/tail-bite.page.ts` and `tail-bite-web-admin.spec.ts`
  — they click `#tailBiteFactor-…-yes|no`; a `<button>` click works the same as the mat toggle's. Adjust only if a
  selector reads `mat-button-toggle` or `aria-pressed`.

**Interfaces:** Consumes Task 1's classes. Produces `onAnswerKeydown(event: KeyboardEvent, factor: TailBiteFactor): void`
and `answerTabIndex(factor: TailBiteFactor, value: boolean): 0 | -1`.

- [ ] **Step 1: Failing specs** (add to the component spec)

```ts
  it('makes the checked option the tab stop, and Ja when unanswered', () => {
    create(detail());
    expect(component.answerTabIndex(TailBiteFactor.Water, true)).toBe(0);
    expect(component.answerTabIndex(TailBiteFactor.Water, false)).toBe(-1);
    component.setAnswer(TailBiteFactor.Water, false);
    expect(component.answerTabIndex(TailBiteFactor.Water, true)).toBe(-1);
    expect(component.answerTabIndex(TailBiteFactor.Water, false)).toBe(0);
  });

  it('moves and selects with the arrow keys', () => {
    create(detail());
    const key = (k: string) => {
      const e = new KeyboardEvent('keydown', {key: k});
      const prevent = vi.spyOn(e, 'preventDefault');
      component.onAnswerKeydown(e, TailBiteFactor.Feed);
      return prevent;
    };
    expect(key('ArrowRight')).toHaveBeenCalled();
    expect(component.draft(TailBiteFactor.Feed).answer).toBe(false); // unanswered → right selects Nej
    key('ArrowLeft');
    expect(component.draft(TailBiteFactor.Feed).answer).toBe(true);
    key('ArrowDown');
    expect(component.draft(TailBiteFactor.Feed).answer).toBe(false);
    expect(key('Tab')).not.toHaveBeenCalled();
  });

  it('answers and changes the answer by click (setAnswer)', () => {
    create(detail());
    component.setAnswer(TailBiteFactor.Water, true);
    expect(component.draft(TailBiteFactor.Water).answer).toBe(true);
    component.setAnswer(TailBiteFactor.Water, false);
    expect(component.draft(TailBiteFactor.Water).answer).toBe(false);
  });

  it('ignores the keys when read-only or busy', () => {
    create(detail({summary: {...detail().summary, closed: true}}));
    component.onAnswerKeydown(new KeyboardEvent('keydown', {key: 'ArrowRight'}), TailBiteFactor.Feed);
    expect(component.draft(TailBiteFactor.Feed).answer).toBeNull();
  });
```

(Check `emptyDraft`'s unanswered value in `tail-bite-assessment.ts` — `null` or `undefined` — and assert that.)

- [ ] **Step 1b:** the disabled state is template-bound (`[disabled]="readOnly || busy"`); assert it in the component spec
  by rendering the template (`TestBed.createComponent`, as other specs in this module do if any; otherwise in the Playwright
  spec: a closed outbreak's `#tailBiteFactor-water-yes` is disabled).
- [ ] **Step 2: Implement** — template, replacing the `mat-button-toggle-group`:

```html
    <div class="segmented" role="radiogroup" [attr.aria-label]="f.label | translate">
      <button
        *ngFor="let o of answerOptions"
        type="button"
        role="radio"
        class="segmented-option"
        [id]="'tailBiteFactor-' + f.factor + '-' + o.id"
        [class.active]="draft(f.factor).answer === o.value"
        [attr.aria-checked]="draft(f.factor).answer === o.value"
        [attr.tabindex]="answerTabIndex(f.factor, o.value)"
        [disabled]="readOnly || busy"
        (click)="setAnswer(f.factor, o.value)"
        (keydown)="onAnswerKeydown($event, f.factor)"
      >{{ o.label | translate }}</button>
    </div>
```

Component: drop `MatButtonToggleModule` from imports; add

```ts
  readonly answerOptions = [
    {id: 'yes', value: true, label: 'Yes'},
    {id: 'no', value: false, label: 'No'},
  ] as const;

  /** Roving tab stop: the checked option; Ja while the factor is unanswered. */
  answerTabIndex(factor: TailBiteFactor, value: boolean): 0 | -1 {
    const answer = this.draft(factor).answer;
    return (answer === null || answer === undefined ? value : answer === value) ? 0 : -1;
  }

  /** Radiogroup keys, as the compliance report's mode switcher: with two options the arrows toggle (wrap). */
  onAnswerKeydown(event: KeyboardEvent, factor: TailBiteFactor): void {
    if (this.readOnly || this.busy) {
      return;
    }
    const current = this.draft(factor).answer;
    const byKey: Record<string, boolean> = {
      ArrowRight: current === false, ArrowDown: current === false,
      ArrowLeft: current !== true, ArrowUp: current !== true,
      Home: true, End: false,
    };
    const next = byKey[event.key];
    if (next === undefined) {
      return;
    }
    event.preventDefault();
    this.setAnswer(factor, next);
    // Focus follows the checked option; the group re-renders the classes on the next change detection, so pick by value.
    const group = (event.currentTarget as HTMLElement).parentElement;
    group?.querySelectorAll<HTMLElement>('.segmented-option')[next ? 0 : 1]?.focus();
  }
```

  (Unanswered + Right/Down → Nej, unanswered + Left/Up → Ja; Ja + Right → Nej; Nej + Right → Ja.)
- [ ] **Step 3: Delete the moved SCSS** from `compliance-report-page.component.scss` (see Files). Keep `.compliance-mode-toggle`.
- [ ] **Step 4: Review gate, commit** `feat(tail-bite): Ja|Nej as the segmented toggle (#1517)`, push, PR to `stable`
  (body: needs eform-angular-frontend PR from Task 1 merged; it is), watch CI incl. Playwright shard `za`, Copilot, merge.

---

## Repository C — plugin PR 2 (`feat/1517-halebid-photos-report`, already holds the spec and this plan)

### Task 3: Photo query and GetPhoto hardening

**Files:**
- Create: `eFormAPI/Plugins/BackendConfiguration.Pn/BackendConfiguration.Pn/Services/TailBite/TailBitePhotoQuery.cs`
- Modify: `…/Services/TailBite/TailBiteRegistrationService.cs` (`GetPhotoAsync`)
- Modify: `…/EformBackendConfigurationPlugin.cs` (register `ITailBitePhotoQuery`)
- Test: `…/BackendConfiguration.Pn.Integration.Test/TailBite/TailBitePhotoQueryTests.cs` (new),
  `…/TailBite/TailBiteRegistrationServiceTests.cs` (GetPhoto cases)

Two commits: first the hardening (GetPhotoAsync + its tests), then the query.

**Interfaces — Produces:**

```csharp
// UploadedDataId is server-only: the host serialises with Newtonsoft.
public sealed record RegistrationPhoto(Guid PhotoUuid, int RegistrationId, [property: Newtonsoft.Json.JsonIgnore] int UploadedDataId);

public interface ITailBitePhotoQuery
{
    /// <summary>
    /// The photos of the given registrations that pass TailBitePhotoOwnership, are not Removed, and whose
    /// registration is not cancelled; oldest first (CreatedAt, then Id). Cancelled registrations get none.
    /// </summary>
    Task<IReadOnlyList<RegistrationPhoto>> ForRegistrationsAsync(IReadOnlyCollection<TailBiteRegistration> registrations);

    /// <summary>The photo by uuid when it passes the same rule; null otherwise (unknown, unstored, removed, orphan, cancelled).</summary>
    Task<TailBiteRegistrationPhoto?> FindShownAsync(Guid photoUuid);
}
```

- [ ] **Step 1: Hardening tests** in `TailBiteRegistrationServiceTests` — `GetPhoto_UnknownUnstoredForeignAndSwitchedOff_AllAnswerTheSameNotFound`:
  seed a stored photo on PropertyId (enabled tree), a reservation (`SdkUploadedDataId = 0`), and a photo on a second
  property where the caller is no worker; then disable the first property for a fourth call. Each of the four
  `GetPhotoAsync` calls throws `TailBiteNotFoundException` with message `"Photo not found."`. A worker on an enabled
  property still gets the stream (existing test keeps passing).
Also add to `TailBiteExceptions.cs`: `public static TailBiteNotFoundException PhotoNotFound() => new("Photo not found.");` on
`TailBiteNotFoundException` — the one photo refusal used by Tasks 3 and 4.

- [ ] **Step 2: Implement hardening**

```csharp
    public async Task<(Stream Content, string ContentType)> GetPhotoAsync(int callerSiteId, Guid photoUuid)
    {
        var photo = await db.TailBiteRegistrationPhotos.AsNoTracking().Where(TailBitePhotoOwnership.IsStored)
                        .FirstOrDefaultAsync(p => p.PhotoUuid == photoUuid && p.WorkflowState != Constants.WorkflowStates.Removed)
                    ?? throw TailBiteNotFoundException.PhotoNotFound();
        // Unknown, unstored, foreign and switched-off answer alike, so the call cannot be used to learn which uuids exist.
        try
        {
            await access.RequireEnabledAsync(photo.PropertyId);
            await access.RequireWorkerAsync(callerSiteId, photo.PropertyId);
        }
        catch (TailBiteForbiddenException) { throw TailBiteNotFoundException.PhotoNotFound(); }
        return await photoStorage.OpenAsync(photo.SdkUploadedDataId);
    }
```

  Update the interface doc comment ("Opens a photo for a worker of the photo's property; any refusal is NotFound.").
  Commit: `fix(tail-bite): GetPhoto answers NotFound for every refusal (#1517)` (after its own review gate).
- [ ] **Step 3: Query tests** (`TailBitePhotoQueryTests : TailBiteTestBase`), each seeding through
  `SeedRegistrationAsync` and photos written directly as `TailBiteRegistrationPhoto` rows:
  - `ForRegistrations_OwnedStoredPhotosOnly`: photos with (a) matching uuid/property/site/stored → included;
    (b) other site, (c) other property, (d) `SdkUploadedDataId = 0`, (e) `WorkflowState = Removed` → excluded.
  - `ForRegistrations_CancelledRegistrationHasNone`.
  - `ForRegistrations_MultiPenRegistrationListedOnce` (registration with rows on Pen309 and Pen310, two photos → two
    entries, one RegistrationId).
  - `FindShown_NullForCancelledOrphanAndUnstored`; `FindShown_ReturnsPhotoAndRegistration`.
- [ ] **Step 4: Implement**

```csharp
public class TailBitePhotoQuery(BackendConfigurationPnDbContext db) : ITailBitePhotoQuery
{
    private const string Removed = Constants.WorkflowStates.Removed;

    public async Task<IReadOnlyList<RegistrationPhoto>> ForRegistrationsAsync(IReadOnlyCollection<TailBiteRegistration> registrations)
    {
        // BelongsTo is per registration; for a batch the same rule is applied in two steps: stored and live in the query,
        // uuid, property and uploading site through the lookup key.
        var shown = registrations.Where(r => r.CancelledAt == null && r.WorkflowState != Removed).DistinctBy(r => r.Id).ToList();
        if (shown.Count == 0) return [];
        var byOwner = shown.ToDictionary(r => (r.ClientUuid, r.PropertyId, r.SiteId), r => r.Id);
        var uuids = shown.Select(r => r.ClientUuid).ToList();
        var photos = await db.TailBiteRegistrationPhotos.AsNoTracking()
            .Where(p => uuids.Contains(p.RegistrationClientUuid) && p.WorkflowState != Removed)
            .Where(TailBitePhotoOwnership.IsStored)
            .OrderBy(p => p.CreatedAt).ThenBy(p => p.Id)
            .Select(p => new { p.PhotoUuid, p.RegistrationClientUuid, p.PropertyId, p.UploadedBySiteId, p.SdkUploadedDataId })
            .ToListAsync();
        return photos
            .Where(p => byOwner.ContainsKey((p.RegistrationClientUuid, p.PropertyId, p.UploadedBySiteId)))
            .Select(p => new RegistrationPhoto(p.PhotoUuid, byOwner[(p.RegistrationClientUuid, p.PropertyId, p.UploadedBySiteId)],
                p.SdkUploadedDataId))
            .ToList();
    }

    public async Task<TailBiteRegistrationPhoto?> FindShownAsync(Guid photoUuid)
        => await db.TailBiteRegistrationPhotos.AsNoTracking().Where(TailBitePhotoOwnership.IsStored)
            .Where(p => p.PhotoUuid == photoUuid && p.WorkflowState != Removed)
            .Where(p => db.TailBiteRegistrations.Any(r => r.ClientUuid == p.RegistrationClientUuid && r.PropertyId == p.PropertyId
                && r.SiteId == p.UploadedBySiteId && r.CancelledAt == null && r.WorkflowState != Removed))
            .FirstOrDefaultAsync();
}
```

  `ClientUuid` is unique (`BackendConfigurationPnDbContext` index), so the `ToDictionary` key cannot collide. Register: `services.AddTransient<Services.TailBite.ITailBitePhotoQuery, Services.TailBite.TailBitePhotoQuery>();`
- [ ] **Step 5: Apply the common rule over gRPC too** — in the same commit, `GetPhotoAsync` looks the photo up with
  `photoQuery.FindShownAsync(photoUuid) ?? throw TailBiteNotFoundException.PhotoNotFound()` instead of its own query (inject
  `ITailBitePhotoQuery` into `TailBiteRegistrationService`; update its test constructors), so a cancelled registration's
  photo or an orphan photo is NotFound in the app as well. Test: `GetPhoto_CancelledRegistrationsPhoto_NotFound`.
- [ ] **Step 6:** `dotnet build` the plugin solution locally; review gate; commit `feat(tail-bite): one query for the photos an outbreak shows (#1517)`.

### Task 4: Registrations DTO with photos, REST photo endpoint, gRPC ListOutbreakPhotos

**Files:**
- Modify: `…/Services/TailBite/TailBiteWebModels.cs`, `…/Services/TailBite/TailBiteWebQueryService.cs`
- Create: `…/Controllers/TailBitePhotosController.cs`
- Modify: `…/Protos/tailbite.proto`, `…/Services/GrpcServices/TailBiteGrpcService.cs`, `…/Services/GrpcServices/TailBiteGrpcMapper.cs`
- Create: `…/Services/TailBite/TailBiteOutbreakPhotoService.cs` (manager-checked list for the app and the REST bytes)
- Test: `…/Integration.Test/TailBite/TailBiteWebQueryServiceTests.cs`, `…/Integration.Test/TailBite/TailBiteOutbreakPhotoServiceTests.cs` (new),
  `…/BackendConfiguration.Pn.Test/Controllers/TailBitePhotosControllerTests.cs` (new),
  `…/BackendConfiguration.Pn.Test/GrpcServices/TailBiteGrpcServiceMappingTests.cs`

**Interfaces:**
- Consumes `ITailBitePhotoQuery` (Task 3).
- Produces (C#):

```csharp
// TailBiteWebModels.cs — Photos reuses RegistrationPhoto (JSON: photoUuid, registrationId).
public sealed record OutbreakRegistrations(int PropertyId, IReadOnlyList<OutbreakRegistrationRow> Rows,
    IReadOnlyList<ActionTypeName> ActionTypes, IReadOnlyList<RegistrationPhoto> Photos);

// Shared helpers (TailBiteOutbreakRows.cs, new), used by Tasks 4 and 8 and by OutbreakRegistrationsAsync:
internal static class TailBiteOutbreakRows
{
    /// <summary>The live link → row → registration join of the given outbreaks, cancelled registrations included.</summary>
    public static IQueryable<OutbreakRowJoin> Of(BackendConfigurationPnDbContext db, IReadOnlyCollection<int> outbreakIds) => ...;
}
public sealed record OutbreakRowJoin(int OutbreakId, TailBiteRegistrationLocation Row, TailBiteRegistration Registration);

// IGrpcSiteResolver extension (TailBiteSiteNames.cs, new): names with the "#id" fallback, one resolver call per site.
public static Task<IReadOnlyDictionary<int, string>> DisplayNamesAsync(this IGrpcSiteResolver resolver, IEnumerable<int> siteIds);

// TailBiteOutbreakPhotoService.cs
public sealed record OutbreakPhoto(Guid PhotoUuid, int RegistrationId, IReadOnlyList<int> LocationIds, DateTime EffectiveAt,
    int SiteId, string SiteName);
public interface ITailBiteOutbreakPhotoService
{
    /// <summary>Manager of an enabled property: the outbreak's shown photos, newest registration first.</summary>
    Task<IReadOnlyList<OutbreakPhoto>> ListAsync(int callerSiteId, int outbreakId);
    /// <summary>Manager of the photo's enabled property; every refusal is TailBiteNotFoundException("Photo not found.").</summary>
    Task<(Stream Content, string ContentType)> OpenForManagerAsync(int callerSiteId, Guid photoUuid);
}
```

- Proto (append; keep field numbers of existing messages):

```proto
  rpc ListOutbreakPhotos(TbListOutbreakPhotosRequest) returns (TbListOutbreakPhotosResponse);
...
// Manager-only: the photos of an outbreak's non-cancelled registrations, newest first. Fetch bytes with GetPhoto.
message TbListOutbreakPhotosRequest { string outbreak_id = 1; }
message TbOutbreakPhoto {
  string photo_uuid = 1; string registration_id = 2; repeated string location_ids = 3;
  google.protobuf.Timestamp effective_at = 4; string site_name = 5;
}
message TbListOutbreakPhotosResponse { repeated TbOutbreakPhoto photos = 1; }
```

- REST: `GET api/backend-configuration-pn/tail-bite/photos/{photoUuid:guid}` → 200 bytes with content type and
  `Content-Disposition: inline`, or 404 with no body.

- [ ] **Step 1: Failing tests**
  - Web query: `OutbreakRegistrations_PhotosPerRegistration_CountFollowsTheCommonRule` — a two-pen registration with two
    owned photos → `Photos` has 2 entries with that RegistrationId; both rows' `PhotoCount` is 2 (the count stays per row
    for the existing DTO, the web shows it on the first row only); a cancelled registration with a photo → `PhotoCount` 0
    and no `Photos` entry; a photo by another site → not counted.
  - Photo service: `List_ManagerSeesNewestFirst_WithPensAndName`, `List_WorkerForeignAndSwitchedOffRefusedAlike`
    (`AssertRefusedAlike`), `OpenForManager_WorkerForeignSwitchedOffCancelledAndUnknown_AllNotFound` (same message).
  - Controller unit test: `Get_NotFoundForbiddenAndUnauthenticated_All404WithoutBody`, `Get_Success_FileWithInlineDisposition`
    (substitute `ITailBiteAccess` and `ITailBiteOutbreakPhotoService`, follow `TailBiteWebControllerTests` style).
  - gRPC mapping test: `ListOutbreakPhotos_MapsIdsTimesAndNames`.
- [ ] **Step 2: Web query** — in `OutbreakRegistrationsAsync`, replace the inline photo lookup with the query:

```csharp
        var photos = await photoQuery.ForRegistrationsAsync(rows.Select(x => x.reg).ToList());
        var photoCount = photos.GroupBy(p => p.RegistrationId).ToDictionary(g => g.Key, g => g.Count());
        ...
            photoCount.GetValueOrDefault(x.reg.Id))).ToList(), actionTypeNames, photos);
```

  Move the join to `TailBiteOutbreakRows.Of` and the private `DisplayNamesAsync` to the resolver extension (behaviour
  unchanged). Add `ITailBitePhotoQuery photoQuery` to the primary constructor; fix every `new TailBiteWebQueryService(...)`
  in tests (`TailBiteWebQueryServiceTests:42`).
- [ ] **Step 3: Photo service**

```csharp
public class TailBiteOutbreakPhotoService(BackendConfigurationPnDbContext db, ITailBiteAccess access, ITailBitePhotoQuery photoQuery,
    ITailBitePhotoStorage storage, IGrpcSiteResolver siteResolver) : ITailBiteOutbreakPhotoService
{
    private const string Removed = Constants.WorkflowStates.Removed;

    public async Task<IReadOnlyList<OutbreakPhoto>> ListAsync(int callerSiteId, int outbreakId)
    {
        var propertyId = await db.TailBiteOutbreaks.AsNoTracking().Where(o => o.Id == outbreakId && o.WorkflowState != Removed)
                             .Select(o => (int?)o.PropertyId).SingleOrDefaultAsync()
                         ?? throw TailBiteForbiddenException.NoAccess();
        await access.RequireEnabledManagerAsync(callerSiteId, propertyId);
        var rows = await TailBiteOutbreakRows.Of(db, [outbreakId]).OrderBy(x => x.Row.Id).ToListAsync();
        var pens = rows.ToLookup(x => x.Registration.Id, x => x.Row.LocationId);
        var byId = rows.Select(x => x.Registration).DistinctBy(r => r.Id).ToDictionary(r => r.Id);
        var photos = await photoQuery.ForRegistrationsAsync(byId.Values.ToList());
        var names = await siteResolver.DisplayNamesAsync(byId.Values.Select(r => r.SiteId));
        // Newest registration first; OrderBy is stable, so photos keep their oldest-first order within one registration.
        return photos.Select(p =>
            {
                var r = byId[p.RegistrationId];
                return new OutbreakPhoto(p.PhotoUuid, r.Id, pens[r.Id].Distinct().ToList(),
                    DateTime.SpecifyKind(r.EffectiveAt, DateTimeKind.Utc), r.SiteId, names[r.SiteId]);
            })
            .OrderByDescending(p => p.EffectiveAt).ThenBy(p => p.RegistrationId)
            .ToList();
    }

    public async Task<(Stream Content, string ContentType)> OpenForManagerAsync(int callerSiteId, Guid photoUuid)
    {
        var photo = await photoQuery.FindShownAsync(photoUuid) ?? throw TailBiteNotFoundException.PhotoNotFound();
        try { await access.RequireEnabledManagerAsync(callerSiteId, photo.PropertyId); }
        catch (TailBiteForbiddenException) { throw TailBiteNotFoundException.PhotoNotFound(); }
        return await storage.OpenAsync(photo.SdkUploadedDataId);
    }
}
```

  Register it.
- [ ] **Step 4: Controller**

```csharp
/// <summary>The bytes of a shown tail-bite photo, for the web outbreak page (manager only). Every refusal is a bare 404.</summary>
[Authorize]
[Route("api/backend-configuration-pn/tail-bite/photos")]
public class TailBitePhotosController(ITailBiteAccess access, ITailBiteOutbreakPhotoService photos) : Controller
{
    [HttpGet("{photoUuid:guid}")]
    public async Task<IActionResult> Get(Guid photoUuid)
    {
        try
        {
            var site = await access.RequireCallerSiteAsync();
            var (content, contentType) = await photos.OpenForManagerAsync(site, photoUuid);
            Response.Headers["Content-Disposition"] = "inline";
            return File(content, contentType);
        }
        // RpcException: GrpcSiteResolver refuses an inactive or resigned account with Unauthenticated (ruling 10).
        catch (Exception e) when (e is TailBiteNotFoundException or TailBiteForbiddenException or RpcException)
        {
            return NotFound();
        }
    }
}
```

- [ ] **Step 5: gRPC** — add `ITailBiteOutbreakPhotoService outbreakPhotos` to `TailBiteGrpcService`'s constructor and

```csharp
    public override Task<TbListOutbreakPhotosResponse> ListOutbreakPhotos(TbListOutbreakPhotosRequest request, ServerCallContext context)
        => RunAsSiteAsync(context, async site =>
        {
            var rows = await outbreakPhotos.ListAsync(site, Id(request.OutbreakId, "outbreak_id")).ConfigureAwait(false);
            var response = new TbListOutbreakPhotosResponse();
            response.Photos.AddRange(rows.Select(MapOutbreakPhoto));
            return response;
        });
```

  and `MapOutbreakPhoto` in `TailBiteGrpcMapper` (ids via `S(...)`, `Timestamp.FromDateTime(utc)`). Update every test that
  constructs `TailBiteGrpcService` (`TailBiteGrpcServiceMappingTests:28`, `TailBiteGrpcServiceCancellationTests:25`).
- [ ] **Step 6:** `dotnet build`; review gate; commit `feat(tail-bite): outbreak photos over REST and gRPC (#1517)`.

### Task 5: Web — thumbnails, gallery and viewer on the outbreak page

**Files:**
- Modify: `eform-client/src/app/plugins/modules/backend-configuration-pn/models/tail-bite/tail-bite.models.ts`
  (`TailBitePhotoRef {photoUuid: string; registrationId: number}`; `photos: TailBitePhotoRef[]` on `TailBiteOutbreakRegistrations`)
- Modify: `…/services/backend-configuration-pn-tail-bite.service.ts` (+ spec): `getPhotoBlob(uuid: string): Observable<Blob>`
  via `this.apiBaseService.getBlobData(this.url(\`photos/${uuid}\`))`
- Modify: `…/tail-bite-outbreak-detail/tail-bite-outbreak-view.ts` (+ spec): row view gains `photos: string[]` (uuids, on the
  registration's first row only; `[]` on other rows and cancelled rows); view gains `gallery: TailBiteGalleryItem[]`
  (`{uuid, registrationId, effectiveAt: Date|null, pens: string, siteName}`, newest first, pens joined `', '`)
- Create: `…/tail-bite/components/tail-bite-photo-thumb/tail-bite-photo-thumb.component.ts` (+ spec) — loads one blob,
  shows an `<img>` via an object URL, revokes it on destroy, shows a placeholder tile (`mat-icon` `broken_image`) on error
- Create: `…/tail-bite/components/tail-bite-photo-viewer/tail-bite-photo-viewer.component.ts|html` (+ spec) — `MatDialog`
  taking `{items: TailBiteGalleryItem[], index: number}` (Task 9 builds the same items from the report model)
  with the image, "previous"/"next" buttons, caption `{date} · {pens} · {name}`, a download link (`download` attribute on an
  `<a>` with the object URL), close button
- Modify: `…/tail-bite-outbreak-detail/tail-bite-outbreak-detail.component.html|ts` (+ spec)
- Modify: i18n `da.ts`, `enUS.ts`, `deDE.ts`: `'Photos from the outbreak'`, `'Previous photo'`, `'Next photo'`,
  `'Download photo'`, `'Photo {{n}} of {{total}}'`, `'The photo could not be loaded'` (check existing keys first)
- Test (Playwright): `eform-client/playwright/e2e/plugins/backend-configuration-pn/za/tail-bite-web-admin.spec.ts` — an outbreak
  with an uploaded photo shows a thumbnail and opens the viewer. Use the existing seeding path of that spec; if it cannot upload
  a photo, add a test-only seed through the gRPC `UploadPhoto` the spec's helpers already call, or skip this Playwright case and
  say so in the PR (do NOT add a production endpoint for tests).

Layout (board "Nyt: udbrud med fotos og Ja|Nej"):
- Fotos column: up to three 48 px thumbnails (`object-fit: cover`, `border-radius: var(--radius-sm)`), then `+n` text
  when more; "–" when none. Clicking a thumbnail opens the viewer at that photo.
- Under the registrations card: a `mat-card` "Fotos fra udbruddet" (hidden when the gallery is empty) with a wrapping grid of
  120 px thumbnails, each captioned `dd.MM HH:mm · pens · name`.
- SCSS: only layout in the component (`display:grid; grid-template-columns: repeat(auto-fill, minmax(120px, 1fr)); gap:
  var(--spacing-md)`), tokens only. Search `eform-angular-frontend/eform-client/src/scss` for an existing thumbnail/gallery
  class first (the compliance image gallery, `compliance-image-gallery`) and reuse it if it fits.

- [ ] **Step 1: Failing specs** — view builder: photos on the first row only, cancelled row `[]`, gallery newest first with
  pens joined; thumb component: creates and revokes the object URL (`URL.createObjectURL`/`revokeObjectURL` spied), error →
  `failed = true`; viewer: next/previous wrap around; detail component: clicking a thumb calls `dialog.open` with the index.
- [ ] **Step 2: Implement** the files above. Object URLs live in the thumb/viewer components only; the viewer fetches its own
  blob for the current photo (re-using one fetched blob per uuid via a `Map<string, Blob>` passed in `data` is fine).
- [ ] **Step 3: Playwright case** as described.
- [ ] **Step 4:** Review gate; commit `feat(tail-bite): photos on the web outbreak page (#1517)`; push; open PR 2 to `stable`
  with Tasks 3–5; watch CI; Copilot; merge on green.

---

## Repository D — flutter-halebid (`feat/outbreak-photos` off `stable`)

Repo: `/home/rene/AndroidStudio/flutter-halebid` (use a worktree in the scratchpad). The plugin PR 2 must be merged first
(the proto is copied from plugin `stable`).

### Task 6: API, repository and demo data

**Files:**
- Modify: `proto/tailbite.proto` (byte copy of the plugin's), regenerate `lib/generated/grpc/*` with `tool/gen_proto.sh`
  (protoc 25.3 — if not available locally, edit the generated files by hand to match exactly what protoc would emit for
  the three new messages and the rpc; the CI `analyze` step catches mistakes)
- Modify: `lib/core/api/tail_bite_api.dart`, `lib/core/demo/demo_tail_bite_api.dart`, `lib/core/demo/demo_aware_tail_bite_api.dart`
- Modify: `lib/features/outbreaks/data/outbreak_repository.dart`, `lib/features/outbreaks/domain/outbreak.dart`
- Create: `assets/demo/photo_1.jpg`, `assets/demo/photo_2.jpg` (generated with ImageMagick: 800×600, a soft gradient in two
  greens/straw tones plus a few blurred shapes; no text, no people, < 60 KB each); `pubspec.yaml` assets entry
- Test: `test/core/api/tail_bite_api_test.dart`, `test/core/demo/demo_tail_bite_api_test.dart`,
  `test/features/outbreaks/outbreak_repository_test.dart`

**Interfaces — Produces:**

```dart
// TailBiteApi
Future<List<TbOutbreakPhoto>> listOutbreakPhotos(String outbreakId);
/// The content type and bytes of a photo (GetPhoto stream joined).
Future<({String contentType, Uint8List bytes})> getPhoto(String photoUuid);

// domain/outbreak.dart
class OutbreakPhoto extends Equatable {
  const OutbreakPhoto({required this.uuid, required this.registrationId, required this.locationIds,
      required this.effectiveAt, required this.siteName});
  final String uuid; final String registrationId; final List<String> locationIds;
  final DateTime effectiveAt; final String siteName;
}

// OutbreakRepository
Future<List<OutbreakPhoto>> photos(String outbreakId);   // mapped failures as the other reads (_guard)
Future<Uint8List> photoBytes(String photoUuid);
```

- [ ] **Step 1: Failing tests** — grpc api joins chunks and reads the content type first; demo `listOutbreakPhotos('31')`
  returns two photos on registration `'601'` and `getPhoto` returns the bundled bytes; `'32'` returns `[]`; repository maps
  an offline failure to the same `OutbreakFailureKind` the other reads use.
- [ ] **Step 2: Implement.** Demo photo uuids are fixed strings (`'00000000-0000-4000-8000-000000000601'`,
  `'…0602'`), `getPhoto` loads `assets/demo/photo_{1,2}.jpg` with `rootBundle`.
- [ ] **Step 3:** `flutter analyze`; review gate; commit `feat(outbreaks): load an outbreak's photos (#1517)`.

### Task 7: Summary card, photo strip and full-screen viewer

**Files:**
- Create: `lib/features/outbreaks/presentation/outbreak_photos_cubit.dart` — states `PhotosLoading`, `PhotosLoaded(List<OutbreakPhoto>,
  Map<String, Uint8List> bytes)`, `PhotosOffline`, `PhotosEmpty`; loads the list then each photo's bytes (in memory only)
- Create: `lib/features/outbreaks/presentation/widgets/outbreak_summary_card.dart` (started `dd.MM 'kl.' HH:mm`, bitten pigs
  "6, heraf 2 alvorlige", pens)
- Create: `lib/features/outbreaks/presentation/widgets/outbreak_photo_strip.dart` (horizontal list of 104 dp tiles captioned
  `{pens} · {dd.MM HH:mm}`; tap opens the viewer)
- Create: `lib/features/outbreaks/presentation/photo_viewer_page.dart` (full screen, `InteractiveViewer`, previous/next, caption
  `{pens} · {date}` and `{name} · foto {n} af {total}`, close)
- Modify: `assessment_page.dart`, `follow_up_page.dart` (insert the card and strip at the top of the scroll content),
  `outbreak_route.dart` (provide the cubit for the outbreak)
- Modify: `lib/l10n/app_{da,en,de}.arb`: `outbreakPhotosTitle` ("Fotos fra stalden"), `outbreakPhotosCount` ("{n} fotos"),
  `outbreakPhotosOffline` ("Fotos kan ikke hentes uden net"), `outbreakSummaryTitle` ("Udbruddet"), `photoViewerPosition`,
  `photoViewerPrevious`, `photoViewerNext`, `photoViewerClose`, `outbreakBittenPigs`; regenerate l10n
- Test: `test/features/outbreaks/outbreak_photos_cubit_test.dart`, `test/features/outbreaks/outbreak_photo_strip_test.dart`,
  `test/features/outbreaks/photo_viewer_page_test.dart`, extend `test/demo_review_flow_test.dart` (the strip shows two photos on
  outbreak 31)
- Goldens: 07_assessment and 08_follow_up change. After pushing, run `gh workflow run update-goldens.yml --ref <branch>`,
  download the artifact as the workflow header describes, commit the changed PNGs (inspect them first).

Rules: nothing is written to disk; `LocalDataCleaner` needs no change. The strip is hidden (no card) when the list is empty;
offline shows the card with the offline text. Touch targets ≥ 48 dp; the viewer's buttons have semantics labels.

- [ ] **Step 1: Failing tests** (cubit: online → loaded with bytes; offline failure → `PhotosOffline`; empty → `PhotosEmpty`;
  strip: renders n tiles and opens the viewer on tap; viewer: next/previous wrap, position text).
- [ ] **Step 2: Implement.**
- [ ] **Step 3:** `flutter analyze`; review gate; commit `feat(outbreaks): photos and a summary on the outbreak screens (#1517)`;
  push; PR to `stable`; update goldens via the workflow; watch CI; Copilot; merge on green. The store release is the owner's.

---

## Repository E — plugin PR 3 (`feat/1517-halebid-compliance-report` off `stable` after PR 2 merged)

### Task 8: Report time helper and Halebid report service

**Files:**
- Create: `…/Services/TailBite/TailBiteComplianceReportModels.cs`
- Create: `…/Services/TailBite/TailBiteComplianceReportService.cs`
- Modify: `…/EformBackendConfigurationPlugin.cs` (register)
- Test: `…/Integration.Test/TailBite/TailBiteComplianceReportServiceTests.cs` (new)

**Interfaces — Produces:**

```csharp
// Time: reuse Infrastructure/Helpers/ComplianceFutureTaskGuard — CopenhagenTimeOf(utc) and UtcFromCopenhagen(local).
// The period is (UtcFromCopenhagen(DateFrom.Date), UtcFromCopenhagen(DateTo.Date.AddDays(1))). No new time helper.

public sealed record TailBiteReportAction(TailBiteFactor Factor, string Description, string ResponsibleName,
    DateTime FollowUpDate, DateTime? DoneAt, string? DoneByName);
public sealed record TailBiteReportRegistration(int Id, DateTime EffectiveAt, string Pens, int Minor, int Severe,
    string ActionTypes, string RegisteredByName, IReadOnlyList<RegistrationPhoto> Photos);
public sealed record TailBiteReportOutbreak(int Id, int PropertyId, string PropertyName, string LocationPath, int RuleVersion,
    DateTime OpenedAt, DateTime? AssessedAt, DateTime? ClosedAt, int BittenPigs, int SeverePigs, FactorAnswers? Answers,
    IReadOnlyList<TailBiteReportAction> Actions, IReadOnlyList<TailBiteReportRegistration> Registrations);
// Actions total/done and the photo count are derived where shown: Actions.Count, Actions.Count(a => a.DoneAt != null),
// Registrations.Sum(r => r.Photos.Count).
public sealed record TailBiteReportTotals(int Outbreaks, int ClosedInPeriod, int BittenPigs, int SeverePigs, int ActionsDone, int ActionsTotal);
public sealed record TailBiteComplianceReport(IReadOnlyList<TailBiteReportOutbreak> Outbreaks, TailBiteReportTotals Totals);

public interface ITailBiteComplianceReportService
{
    /// <summary>
    /// The Halebid section for the caller, or null when the caller sees none (no tail-bite identity, or no property in
    /// the filter where they are a manager and tail biting is enabled). Never throws TailBiteException.
    /// </summary>
    Task<TailBiteComplianceReport?> BuildAsync(ComplianceReportRequestModel request);
}
```

All `DateTime`s in the model are UTC with `DateTimeKind.Utc` (the web formats in the browser's zone = Copenhagen for the
users; the export converts with `ComplianceFutureTaskGuard.CopenhagenTimeOf`).

- [ ] **Step 1: Failing tests** (`TailBiteComplianceReportServiceTests : TailBiteTestBase`; the caller site is set through a
  substituted `IGrpcSiteResolver`/`ITailBiteWorkerEmailCounter` as `TailBiteAccessTests` does):
  - Period edges (period 2026-11-01..2026-11-30, CET = UTC+1): opened 2026-12-01 00:10 local (`2026-11-30T23:10Z`) → out;
    closed 2026-10-31 23:50 local (`2026-10-31T22:50Z`) → out; opened in October and still open → in; closed
    2026-11-01 00:05 local (`2026-10-31T23:05Z`) → in and counted in ClosedInPeriod; opened 2026-11-30 23:30 local
    (`2026-11-30T22:30Z`) → in.
  - Access: manager → report; worker without the flag → null; no identity (`GetSdkSiteIdAsync` 0) → null; resolver throws
    `RpcException(Unauthenticated)` (inactive account) → null; ambiguous email (count 2) → null; switched-off property → null; `PropertyId = null` with the caller manager on one of two
    enabled properties → only that property's outbreaks.
  - Manager on an enabled property with no outbreaks in the period → `Outbreaks` empty, not null.
  - Withdrawn actions excluded from `Actions` and the totals; cancelled registrations excluded from `Registrations`,
    `BittenPigs` and the photos.
  - A two-pen registration is one `TailBiteReportRegistration` with `Pens = "Sti 309, Sti 310"`.
  - `LocationPath` for an outbreak on `SectionId` is `"Stald A › Sektion 4"`.
- [ ] **Step 2: Implement** `TailBiteComplianceReportService(BackendConfigurationPnDbContext db, ITailBiteAccess access,
  ITailBitePhotoQuery photoQuery, IGrpcSiteResolver siteResolver)`:
  1. `int site; try { site = await access.RequireCallerSiteAsync(); } catch (Exception e) when (e is TailBiteForbiddenException or RpcException) { return null; }`
     (ruling 10).
  2. Properties: `TailBiteProperties.Where(Enabled)` ∩ (`request.PropertyId` or all) ∩ properties where
     `access.IsManagerAsync(site, p)`; empty → `null`. Property names from `db.Properties`.
  3. `start = UtcFromCopenhagen(request.DateFrom.Date)`, `end = UtcFromCopenhagen(request.DateTo.Date.AddDays(1))`; outbreaks
     `PropertyId ∈ set && WorkflowState != Removed && OpenedAt < end && (ClosedAt == null || ClosedAt >= start)`, ordered
     `OpenedAt, Id`.
  4. Links → rows → registrations through `TailBiteOutbreakRows.Of` (Task 4). Bitten/severe sums over rows of
     non-cancelled registrations.
  5. Assessment: the live `TailBiteRiskAssessments` row per outbreak (`WorkflowState != Removed`); actions of that
     assessment with `WithdrawnAt == null && WorkflowState != Removed`, ordered `FollowUpDate, Id`.
  6. Photos: one `photoQuery.ForRegistrationsAsync(registrations)` call.
  7. Location path: load the property's `TailBiteLocations` (removed included) once; walk `ParentId` to the root, drop the
     root, join `" › "`. Pens of a registration: its rows' location names, distinct, in row order, joined `", "`.
  8. Action-type names per registration from `TailBiteRegistrationActions` + `TailBiteActionTypes`, joined `", "`; "–" never
     stored (empty string = none).
  9. Names through `siteResolver.DisplayNamesAsync(...)` (Task 4's extension).
  10. Totals as in the spec.
- [ ] **Step 3:** register `ITailBiteComplianceReportService`; `dotnet build`; review gate; commit
  `feat(compliance): Halebid outbreaks for the report period (#1517)`.

### Task 9: Endpoint and web Rapport section

**Files:**
- Modify: `…/Controllers/ComplianceReportController.cs` — inject `ITailBiteComplianceReportService`; add

```csharp
    /// <summary>
    /// The Halebid section of Rapport (#1517): the period's outbreaks on the properties where the caller is a tail-bite
    /// manager. Model null = the caller sees no Halebid section. The service never throws for access; anything else is the
    /// host's 500, as for the other report endpoints.
    /// </summary>
    [HttpPost("tail-bite")]
    public async Task<OperationDataResult<TailBiteComplianceReport>> TailBite([FromBody] ComplianceReportRequestModel requestModel)
        => requestModel is null
            ? new OperationDataResult<TailBiteComplianceReport>(false, "Request body is required.")
            : new OperationDataResult<TailBiteComplianceReport>(true, await _tailBiteReport.BuildAsync(requestModel));
```

- Modify Angular: `models/compliance-report/compliance-report.model.ts` (TS mirrors of the records; `answers` nullable;
  photos `{photoUuid, registrationId}`), `services/backend-configuration-pn-compliance-report.service.ts` (+ spec): `tailBite(model)` posting to
  `api/backend-configuration-pn/compliance-report/tail-bite` with `postNoToast`.
- Create: `modules/compliance-report/components/compliance-tail-bite-section/compliance-tail-bite-section.component.{ts,html,scss,spec.ts}`
  as the board "Nyt: halebid i compliance-rapporten": card titled "Halebid: udbrud i perioden"; four stat tiles (udbrud i
  perioden, lukket, bidte grise (heraf alvorlige), handlinger udført "d af t"); a table, one row per outbreak (ID, Lokation,
  Startet, Risikovurderet, Lukket, Bidte grise, Ja-faktorer, Handlinger, Billeder) with an expand button revealing factors,
  actions and photo thumbnails (reuse PR 2's `TailBitePhotoThumbComponent` and viewer; build each photo's `TailBiteGalleryItem`
  from its registration: effectiveAt, pens, registeredByName); "Ingen udbrud i perioden" when the list
  is empty; nothing at all when the model is null.
- Modify: `compliance-report-view.component.html|ts` (+ spec): render `<app-compliance-tail-bite-section>` after the sections;
  it fetches on the same trigger as the view (`fetchRequested$` / refresh) with `state.requestModel`, independent of the eForm
  fetch (a failure of one does not hide the other; a failed Halebid fetch shows nothing and logs nothing to the user).
- Modify i18n (da, enUS, deDE): `'Tail biting: outbreaks in the period'`, `'No outbreaks in the period'`, `'Outbreaks in the period'`,
  `'Closed in the period'`, `'Bitten pigs'`, `'of which {{n}} severe'`, `'Actions done'`, `'{{done}} of {{total}} done'`,
  `'Risk assessed'`, `'Yes factors'`, `'Not assessed'`, `'None'` (check every key exists first; the tail-bite module already
  has factor labels — reuse them).
- Test (Playwright): `eform-client/playwright/e2e/plugins/backend-configuration-pn/za/` — new spec or a case in
  `tail-bite-web-admin.spec.ts`: after the existing outbreak flow, open the compliance report, Rapport, the period covering
  today → `#complianceTailBiteSection` is visible with the outbreak's row.

- [ ] **Step 1: Failing specs** (section: null → nothing rendered; empty → "Ingen udbrud…"; data → rows, totals; expand toggles).
- [ ] **Step 2: Implement.** SCSS tokens only, layout only; search `eform-angular-frontend` for existing stat-tile classes
  (the compliance Oversigt stats) and reuse them.
- [ ] **Step 3:** Review gate; commit `feat(compliance): Halebid section in Rapport (#1517)`.

### Task 10: Export — document model, builder, writers

**Files:**
- Modify: `…/Infrastructure/Models/ComplianceReport/ComplianceExportDocument.cs`:
  - `ComplianceExportTable`: `bool PdfOnly`, `string Lead = ""`, `bool StartsOnNewPage`
  - `ComplianceExportColumn`: `bool PdfOnly`
  - `ComplianceExportImageBlock`: `List<int> TailBitePhotos = []` (SDK UploadedData ids; when non-empty `ImageNames` is empty)
- Create: `…/Services/BackendConfigurationComplianceExportService/ComplianceExportTailBiteBuilder.cs` (static, pure)
- Modify: `ComplianceExportDocumentBuilder.ImagesCell` → `internal` (the Halebid builder reuses it)
- Modify: `ComplianceExportDocumentBuilder.BuildReport` — new optional parameter `TailBiteComplianceReport? tailBite = null`;
  after the eForm groups call `ComplianceExportTailBiteBuilder.Append(document, tailBite, includeImageAppendix, localizationService)`
- Modify: `ComplianceExportCsvWriter.Write` — skip tables with `PdfOnly` and columns with `PdfOnly` (both in the union and when
  placing cells; a skipped column's cell is skipped by INDEX as the Word writer does for `CsvOnly`); the header line is
  written only when at least one table is not `PdfOnly` (else a Halebid-only empty state would write a blank `\r\n` line)
- Modify: `ComplianceExportWordWriter` — constructor gains `ITailBitePhotoStorage? tailBitePhotoStorage = null`; prints
  `Lead` after the title (`<p style='font-size:9pt;text-align:left;'>`); `StartsOnNewPage` adds `page-break-before:always` to
  the table's first heading; a table with no columns prints no `<table>`; `DocumentHasImages` also counts `TailBitePhotos`;
  `AppendAppendixBlocks` uses one count `block.ImageNames.Count + block.TailBitePhotos.Count` for the `(n/total)` caption
  and the grid loop (index into whichever list is non-empty), and embeds `TailBitePhotos` through a new `InsertImage(Stream, StringBuilder, int, int)` overload that
  shares the resize/encode code with the name-based one (extract the MagickImage part into a private method taking a Stream)
- Modify: `BackendConfigurationComplianceExportService` — inject `ITailBiteComplianceReportService` and `ITailBitePhotoStorage`;
  Rapport arm: `var tailBite = await tailBiteReport.BuildAsync(BuildReportRequest(requestModel, false));` then pass to
  `BuildReport`; PDF arm: `new ComplianceExportWordWriter(localizationService, logger, tailBitePhotoStorage)`
- Modify: `Resources/localization.json` (da, en-US, de) and `ExportLocalizationCompletenessTests` key list
- Test: `ComplianceExportDocumentBuilderTests` (Halebid tables), `ComplianceExportWriterTests` (CSV header/rows, PdfOnly skipped,
  Word lead/new page/no-column table), `ComplianceExportImageEmbeddingTests` (stream overload, shared budget),
  `ComplianceExportServiceTests` (constructor + Rapport arm passes the section), `ExportLocalizationCompletenessTests`

**Builder contract (`ComplianceExportTailBiteBuilder.Append`):**
- `tailBite == null` → document unchanged (Review Focus 5: byte-identical eForm CSV).
- Otherwise table 1 "Udbrud": `Caption = L("TailBite")` ("Halebid"), `Title = L("TailBiteReportTitle")` ("Halebid: udbrud i
  perioden"), `AppendixLabel = L("TailBite")`, `StartsOnNewPage = true`, `Lead` = the totals sentence
  `L("TailBiteTotals", n, c, b, s, d, t)` ("{0} udbrud var åbne i perioden, {1} blev lukket. {2} bidte grise, heraf {3}
  alvorlige. {4} af {5} handlinger udført."), `Subtitle = L("TailBiteOutbreaks")` ("Udbrud").
  Columns, in order (Key / Header key / Type / flags) — the CSV-only ones reproduce today's leading columns so the CSV
  header starts the same with or without eForm tables (ruling 7):
  `SubReport`/`SubReport`/Text/CsvOnly · `CaseId`/`CaseId`/Number · `Property`/`Property`/Text/CsvOnly ·
  `DoneBy`/`DoneBy`/Text/CsvOnly (always empty) · `CompletedDate`/`CompletedDate`/Date/CsvOnly (always empty) ·
  `Area`/`Area`/Text/CsvOnly (location path) · `Images`/`Images`/Number/CsvOnly (`ImagesCell`) ·
  `TailBiteLocation`/`Location`/Text/PdfOnly (location path) · `TailBiteRuleVersion`/`TailBiteRule`/Text/PdfOnly ·
  `TailBiteStarted`/`TailBiteStarted`/Text · `TailBiteAssessed`/`TailBiteAssessed`/Date · `TailBiteClosed`/`TailBiteClosed`/Date ·
  `TailBiteBitten`/`TailBiteBitten`/Number · `TailBiteSevere`/`TailBiteSevere`/Number ·
  `TailBiteYesFactors`/`TailBiteYesFactors`/Text/PdfOnly ·
  `TailBiteWater|Feed|ActivityMaterial|Climate|Health|Management`/factor label keys/Text/CsvOnly ·
  `TailBiteActions`/`TailBiteActions`/Number (PDF DisplayText "d af t udført", "–" when t = 0; CSV the total) ·
  `TailBiteActionsDone`/`TailBiteActionsDone`/Number/CsvOnly · `TailBitePhotos`/`Images`/Number/PdfOnly (`ImagesCell`).
  The PDF thus reads ID, Lokation, Regel, Startet, Risikovurderet, Lukket, Bidte grise, Alvorlige, Ja-faktorer, Handlinger,
  Billeder (the mockup) and the CSV Delrapport, ID, Ejendom, Udført af, Udført dato, Område, Billeder, <answers…>, Startet,
  Risikovurderet, Lukket, Bidte grise, Alvorlige, the six factors, Handlinger, Handlinger udført (the spec).
  Cells: SubReport "Halebid"; Started `Text = local.ToString("yyyy-MM-dd HH:mm")`, `DisplayText = local.ToString("dd.MM.yyyy HH:mm")`;
  dates are local dates; rule `v{n}`; factor cells `L("Yes")`/`L("No")`, empty when not assessed; yes-factors per ruling 6;
  `IsDone = ClosedAt != null`. Pin both headers (with and without eForm tables) in tests.
- Table 2 "Handlinger fra risikovurderingerne" (`PdfOnly = true`, `Subtitle` only): Udbrud, Lokation, Faktor, Handling,
  Ansvarlig, Frist (Date), Udført (Date), Udført af; `IsDone` when done. Omitted when no outbreak has actions.
- Table 3 "Registreringer bag udbruddene" (`PdfOnly`): Udbrud, Dato (Text `dd.MM.yyyy HH:mm` local), Lokation (pens),
  Mindre, Alvorlige (Number), Handling, Registreret af, Billeder (`ImagesCell`). Omitted when there are none.
- Empty state (`Outbreaks.Count == 0`): only table 1 with `PdfOnly = true`, no columns, `Lead = L("TailBiteNoOutbreaks")`.
- Appendix (only when `includeImageAppendix`): on table 1, one block per registration with photos, in outbreak then
  registration order, `Caption = $"{L("TailBiteOutbreak")} {id} · {pens} · {dd.MM.yyyy HH:mm} · {name}"`, `TotalImages` = the
  registration's photo count, `TailBitePhotos` = up to `min(MaxAppendixImagesPerCase, MaxAppendixImages - embedded)` ids;
  `AppendixImagesRequested` += wanted, `AppendixImagesEmbedded` += taken (same accounting as `BuildImageBlock`). A
  registration linked to two outbreaks gets ONE block (a `HashSet<int>` of registration ids, like `casesWithAnAppendixBlock`).

New localisation keys (da / en-US / de): `TailBite` (Halebid / Tail biting / Schwanzbeißen), `TailBiteReportTitle`,
`TailBiteTotals`, `TailBiteNoOutbreaks` (Ingen udbrud i perioden), `TailBiteOutbreaks` (Udbrud), `TailBiteActionsTable`
(Handlinger fra risikovurderingerne), `TailBiteRegistrationsTable` (Registreringer bag udbruddene), `TailBiteOutbreak` (Udbrud),
`Location` (Lokation — check it exists), `TailBiteRule` (Regel), `TailBiteStarted` (Startet), `TailBiteAssessed`
(Risikovurderet), `TailBiteClosed` (Lukket), `TailBiteBitten` (Bidte grise), `TailBiteSevere` (Alvorlige), `TailBiteYesFactors`
(Ja-faktorer), `TailBiteNotAssessed` (Mangler vurdering), `TailBiteNoYesFactors` (Ingen), `TailBiteWater` (Vand), `TailBiteFeed`
(Foder), `TailBiteActivityMaterial` (Legemateriale), `TailBiteClimate` (Klima), `TailBiteHealth` (Sundhed), `TailBiteManagement`
(Ledelse), `TailBiteActions` (Handlinger), `TailBiteActionsDone` (Handlinger udført), `TailBiteActionsOf` ({0} af {1} udført),
`TailBiteFactor` (Faktor), `TailBiteAction` (Handling), `TailBiteResponsible` (Ansvarlig), `TailBiteFollowUp` (Frist),
`TailBiteDone` (Udført), `TailBiteDoneBy` (Udført af), `TailBiteDate` (Dato), `TailBiteMinor` (Mindre), `TailBiteActionType`
(Handling), `TailBiteRegisteredBy` (Registreret af), `Yes`/`No` (check they exist). Reuse an existing key wherever one has the
same Danish text.

- [ ] **Step 1: Failing tests** listed above, including: CSV with eForm rows + Halebid → header line equals
  `Delrapport;ID;Ejendom;Udført af;Udført dato;Område;Billeder;<answers…>;Startet;Risikovurderet;Lukket;Bidte grise;Alvorlige;Vand;Foder;Legemateriale;Klima;Sundhed;Ledelse;Handlinger;Handlinger udført`
  (with the test's localisation map) and a Halebid row like the mockup's `Halebid;41;Eksempelgården I/S;;;Stald 3 › Sektion 2;4;…`;
  no Handlinger/Registreringer rows in the CSV; `tailBite = null` → CSV bytes equal to the pre-change output for the same
  groups (compare against `BuildReport(groups, …)` without the argument); Word: the Halebid caption paragraph carries
  `page-break-before:always`; the lead paragraph follows the title; the empty-state table emits no `<table>`; two registrations
  with 3 photos each and a remaining budget of 4 → first block 3, second block 1, requested 6, embedded 4; a Halebid block's
  caption has no `(n/total)` when all its photos went in; a Halebid-only empty-state CSV is the BOM and nothing else.
- [ ] **Step 2: Implement** the files above.
- [ ] **Step 3:** `dotnet build`; review gate; commit `feat(compliance): Halebid in the PDF and CSV export (#1517)`; push; PR 3
  to `stable`; watch CI; Copilot; merge on green.

---

## Order and handoffs

1 → (merge A) → 2 → (merge B) → 3, 4, 5 → (merge C) → 6, 7 → (merge D) ; 8, 9, 10 → (merge E). D and E are independent after C.
The owner deploys the frontend before the plugin, and releases the app.
