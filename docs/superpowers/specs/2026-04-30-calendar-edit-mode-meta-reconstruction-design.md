# Edit-mode meta reconstruction for custom calendar repeat rules

## Context

The calendar's task-edit modal cannot reconstruct a `CalendarRepeatMeta` from a backend-loaded task. When the user re-opens an existing task whose `repeatRule === 'custom'`, the modal can't tell what the rule was — only that it's "some custom thing" — so the dropdown either pops the custom-config modal automatically or shows defaults. The user has to reconfigure from scratch.

Two root causes:

1. **Multi-day weekly is lost on save.** `AreaRulePlanning` only persists `DayOfWeek` (a single int). For `weeklyMulti`/`everyNWeekMulti` patterns ("Mon, Wed, Fri"), the day list is dropped. Loading them back yields at best a single-day rule.
2. **Even what is persisted is omitted from the response DTO.** `CalendarTaskResponseModel` returns `repeatType` and `repeatEvery` but NOT `RepeatEndMode`, `RepeatOccurrences`, `RepeatUntilDate`, or `DayOfMonth`. So the frontend sees only a fragment of the rule.

The chosen fix (option C — discrete columns, picked over a JSON blob) adds two new columns to `AreaRulePlanning`, extends the response DTO, and adds a frontend reconstruction helper. This preserves the relational schema (no opaque blobs) and lets the backend recurrence calculator continue using discrete fields.

## Approach

### Step 0 — prerequisite audit (blocks the rest)

Before any code changes, **audit the existing recurrence-expansion iterator** in `Microting.EformBackendConfigurationBase` (search for `RepeatType == 2` or wherever `AreaRulePlanning` is materialized into per-week occurrence dates). The question to answer:

> Does the iterator support multi-day weekly rules from a list of weekdays, or does it only ever expand from the single `DayOfWeek` int?

Three branches based on the answer:

1. **Iterator already supports multi-day weekly** (e.g. by reading `RepeatWeekdaysCsv` or by being structured to iterate weekdays generically) → great, proceed with the plan as-written.
2. **Iterator hardcodes `DayOfWeek` as a single int and is small/contained** → bring the expansion fix into this same PR. Read the new `RepeatWeekdaysCsv` and iterate over the parsed list when populated, falling back to `DayOfWeek` when null.
3. **Iterator hardcodes `DayOfWeek` and is too large to fix here** → DO NOT ship multi-day-weekly UI without expansion. Either (a) disable the multi-day weekly controls in the custom modal until expansion lands, or (b) ship as a feature flag with a clear "preview / not yet generating multiple instances" banner. Open a separate ticket for the expansion work.

**This audit must produce a written decision before Layer 1 is touched.** The rest of the spec assumes branch 1 or 2.

Three layers in two repos. Sequence: audit → base → backend plugin → frontend. Each step blocks the next.

### Layer 1 — Base (`eform-backendconfiguration-base`)

New property on `AreaRulePlanning`:

```csharp
public string? RepeatWeekdaysCsv { get; set; }   // varchar(13), "1,3,5" — JS getDay() values
```

Nullable. Only set for `weeklyMulti`/`everyNWeekMulti` and the built-in "weekdays" (Mon-Fri) option. CSV chosen over bitmask because it matches the existing `DayOfWeek`/`DayOfMonth` style and is human-readable in DB; queries don't need set-membership today.

**`RepeatMonth` is dropped from v1.** It would be redundant with `task.taskDate.getUTCMonth()` (the modal can't expose month independently in v1), and adding nullable columns to MariaDB is instant DDL — a future migration if the modal ever decouples "rule month" from "first occurrence month" is cheap.

**EF migration generated via the existing factory** — never hand-written:

```bash
cd Microting.EformBackendConfigurationBase
dotnet ef migrations add AddRepeatWeekdaysCsvToAreaRulePlanning \
  --context BackendConfigurationPnDbContext
```

Tag and publish a new NuGet base version after merge.

### Layer 2 — Backend plugin (`eform-backendconfiguration-plugin`)

**DTO `CalendarTaskResponseModel`** extended with the full repeat surface:

```csharp
public int? RepeatType { get; set; }
public int? RepeatEvery { get; set; }
public int? RepeatEndMode { get; set; }
public int? RepeatOccurrences { get; set; }
public DateTime? RepeatUntilDate { get; set; }
public int? DayOfWeek { get; set; }
public int? DayOfMonth { get; set; }
public string? RepeatWeekdaysCsv { get; set; }
public string? RepeatRule { get; set; }
```

`CalendarController.GetTasksForWeek` mapper populates all fields from `AreaRulePlanning`.

`repeatType` enum reference (existing): `0=none, 1=daily, 2=weekly, 3=monthly, 4=yearly, 5=weekdays, 6=custom`. **Note:** `RepeatType=6` is never persisted by the current frontend save path — when `customRepeatMeta` exists, `resolvedRepeatType` is remapped to `1/2/3/4` via the existing `kindMap` so backend recurrence math sees a coherent type. `RepeatType=6` only persists in the (degenerate) case where `repeatRule='custom'` is set without a meta — reconstruction returns `null` for that row.

**Create/update endpoints** (`CalendarController.CreateTask` / `UpdateTask`) accept the corresponding fields on the request models and write them to the entity. No change to recurrence-expansion code in v1 (see Risks).

### Layer 3 — Frontend (`eform-angular-frontend` host app)

**Models**

- `CalendarTaskModel` (response shape) — add the new fields above.
- `CalendarTaskCreateModel` / `CalendarTaskUpdateModel` (request shapes) — add `repeatWeekdaysCsv?: string`, `repeatMonth?: number`.

**Save flow** (`task-create-edit-modal.component.ts:onSave`)

The chosen dropdown option determines how `repeatWeekdaysCsv` is populated:

```ts
const isCustomRule = repeatRuleValue === 'custom' || repeatRuleValue === 'customCurrent';
const repeatWeekdaysCsv = (isCustomRule && this.customRepeatMeta?.weekdays?.length)
  ? this.customRepeatMeta.weekdays.join(',')
  : null;
```

**Important — explicit clearing rule:** when the user changes the dropdown from a `customCurrent` selection (e.g. weeklyMulti) to a built-in option (e.g. weeklyOne), the existing `repeatWeekdaysCsv` on the row must be cleared. The expression above ensures that — `isCustomRule` is false on a non-custom save, so the payload writes `null` regardless of whatever stale `customRepeatMeta` may still be in memory.

If the modal's lifecycle ever leaves `customRepeatMeta` populated after a non-custom selection, that's a separate concern and doesn't corrupt the row because the save explicitly nulls it.

To keep memory clean, when `repeatControl.valueChanges` fires with a non-custom value (i.e. not `'custom'` and not `'customCurrent'`), also `this.customRepeatMeta = null` to prevent re-using stale meta on a subsequent reopen.

**Reconstruction helper** in `CalendarRepeatService`:

```ts
reconstructMetaFromTask(task: CalendarTaskModel): CalendarRepeatMeta | null
```

Logic:

```ts
const r = task.repeatRule;                   // primary kind signal
const n = task.repeatEvery ?? 1;
const endMode = (['never','after','until'] as const)[task.repeatEndMode ?? 0];
const afterCount = endMode === 'after' ? task.repeatOccurrences ?? undefined : undefined;
const untilTs = endMode === 'until' && task.repeatUntilDate
  ? new Date(task.repeatUntilDate).getTime() : undefined;

if (!r || r === 'none') return null;

// Helper for yearly month — getUTCMonth avoids the local-tz off-by-one
// for ISO timestamps with a `Z` suffix.
const monthFromTaskDate = () => new Date(task.taskDate).getUTCMonth();

switch (r) {
  case 'daily':
    return {kind: n === 1 ? 'daily' : 'everyNd', n, endMode, afterCount, untilTs};

  case 'weeklyOne':
    if (task.dayOfWeek == null) return null;  // malformed legacy row
    return {kind: n === 1 ? 'weeklyOne' : 'everyNWeekOne', n,
            weekday: task.dayOfWeek, endMode, afterCount, untilTs};

  case 'weeklyAll':
    return {kind: n === 1 ? 'weeklyAll' : 'everyNWeekAll', n, endMode, afterCount, untilTs};

  case 'weekdays':
    // "Alle hverdage" = Mon-Fri. Map to weeklyMulti with [1..5] so the
    // formatter renders "Ugentligt hver mandag, tirsdag, onsdag, torsdag og
    // fredag" instead of incorrectly collapsing to "Ugentlig på alle dage".
    return {kind: n === 1 ? 'weeklyMulti' : 'everyNWeekMulti', n,
            weekdays: [1,2,3,4,5], endMode, afterCount, untilTs};

  case 'monthlyDom':
    if (task.dayOfMonth == null) return null;
    return {kind: n === 1 ? 'monthlyDom' : 'everyNMonthDom', n,
            dom: task.dayOfMonth, endMode, afterCount, untilTs};

  case 'yearlyOne':
    if (task.dayOfMonth == null) return null;
    return {kind: n === 1 ? 'yearlyOne' : 'everyNYear', n,
            dom: task.dayOfMonth,
            month: monthFromTaskDate(),
            endMode, afterCount, untilTs};

  case 'custom': {
    const days = (task.repeatWeekdaysCsv ?? '').split(',').filter(Boolean).map(Number);
    switch (task.repeatType) {
      case 1: return {kind: n === 1 ? 'daily' : 'everyNd', n, endMode, afterCount, untilTs};
      case 2:
        if (days.length === 0) return {kind: n === 1 ? 'weeklyAll' : 'everyNWeekAll', n, endMode, afterCount, untilTs};
        if (days.length === 1) return {kind: n === 1 ? 'weeklyOne' : 'everyNWeekOne', n, weekday: days[0], endMode, afterCount, untilTs};
        return {kind: n === 1 ? 'weeklyMulti' : 'everyNWeekMulti', n, weekdays: days, endMode, afterCount, untilTs};
      case 3:
        if (task.dayOfMonth == null) return null;
        return {kind: n === 1 ? 'monthlyDom' : 'everyNMonthDom', n, dom: task.dayOfMonth, endMode, afterCount, untilTs};
      case 4:
        if (task.dayOfMonth == null) return null;
        return {kind: n === 1 ? 'yearlyOne' : 'everyNYear', n,
                dom: task.dayOfMonth,
                month: monthFromTaskDate(),
                endMode, afterCount, untilTs};
      default: return null;
    }
  }

  default: return null;  // unknown repeatRule string
}
```

**Edit-mode init** (`task-create-edit-modal.component.ts:ngOnInit`, around line 175-200)

When `task` has a non-`'none'` `repeatRule`:

```ts
const reconstructed = this.repeatService.reconstructMetaFromTask(task);
if (reconstructed) {
  this.customRepeatMeta = reconstructed;
  this.repeatOptions = this.repeatService.buildRepeatSelectOptions(baseDate, reconstructed);
  this.repeatControl.setValue('customCurrent', {emitEvent: false});
} else {
  this.repeatControl.setValue(task.repeatRule ?? 'none');
}
```

The conditional branch replaces the unconditional `this.repeatControl.setValue(task.repeatRule ?? 'none')` at line 179. With reconstruction, the dropdown lands on the synthesized `'customCurrent'` option (showing the formatted summary like `Ugentligt hver mandag, tirsdag og onsdag`) and clicking `Tilpasset…` pre-populates the modal via the meta hydration shipped in `b7ebea6b`.

## Critical files

**Base repo** (`/home/rene/Documents/workspace/microting/eform-backendconfiguration-base/`):
- `Microting.EformBackendConfigurationBase/Infrastructure/Data/Entities/AreaRulePlanning.cs` (or wherever the entity lives)
- `Microting.EformBackendConfigurationBase/Migrations/AddRepeatWeekdaysAndMonthToAreaRulePlanning.{cs,Designer.cs}` (auto-generated)
- Snapshot file (auto-updated)

**Plugin source repo** (`/home/rene/Documents/workspace/microting/eform-backendconfiguration-plugin/`):
- `eFormAPI/Plugins/BackendConfiguration.Pn/Infrastructure/Models/Calendar/CalendarTaskResponseModel.cs`
- `eFormAPI/Plugins/BackendConfiguration.Pn/Infrastructure/Models/Calendar/CalendarTaskCreateRequestModel.cs`
- `eFormAPI/Plugins/BackendConfiguration.Pn/Infrastructure/Models/Calendar/CalendarTaskUpdateModel.cs`
- `eFormAPI/Plugins/BackendConfiguration.Pn/Controllers/CalendarController.cs` — `GetTasksForWeek`, `CreateTask`, `UpdateTask` mapping
- `Microting.EformBackendConfigurationBase` NuGet ref bumped to the new version

**Frontend (host app)** (`/home/rene/Documents/workspace/microting/eform-angular-frontend/eform-client/src/app/plugins/modules/backend-configuration-pn/`):
- `models/calendar/calendar-task.model.ts`
- `models/calendar/calendar-task-create.model.ts`
- `models/calendar/calendar-task-update.model.ts`
- `modules/calendar/services/calendar-repeat.service.ts` — add `reconstructMetaFromTask`
- `modules/calendar/services/calendar-repeat.service.spec.ts` — round-trip tests
- `modules/calendar/modals/task-create-edit-modal/task-create-edit-modal.component.ts` — `ngOnInit` edit-mode branch + `onSave` write-through

## Reusable pieces

- `buildMetaFromCustomConfig` (forward mapping) and `decomposeCustomMeta` (decomposition for the modal hydration in `b7ebea6b`) — together they form the canonical kind-derivation rules. The new `reconstructMetaFromTask` mirrors `buildMetaFromCustomConfig`'s kind-selection logic.
- `formatCustomRepeatLabel` — unchanged; reads the reconstructed meta and shows the readable summary.
- The `'customCurrent'` synthesized dropdown option (added in `eb62f6cc`) — is the landing slot for any reconstructed meta.

## Verification

1. **C# integration test** in `eFormAPI/Plugins/BackendConfiguration.Pn.Test/` (next to `CalendarResizeTests`): `CalendarRepeatPersistenceTests`
   - Create a task via `CreateTask` with `RepeatType=2, RepeatEvery=2, RepeatRule="custom", RepeatWeekdaysCsv="1,3,5", RepeatEndMode=1, RepeatOccurrences=10` (RepeatType=2 because the existing kindMap collapses `weeklyMulti`/`everyNWeekMulti` to type 2; never use 6 in v1).
   - Call `GetTasksForWeek` for a week containing the task.
   - Assert all those fields round-trip exactly.
   - Use the existing Testcontainers MariaDB pattern.
2. **Frontend Karma unit test** in `calendar-repeat.service.spec.ts`: round-trip every `kind` — `buildMetaFromCustomConfig` → save-payload-shape → `reconstructMetaFromTask(fakeTask)` → equality check. Covers all 12 kinds plus the three end modes.
   - **Plus null-returning cases**: `repeatRule==='none'` → null; `repeatRule===undefined` (legacy) → null; `repeatRule==='someUnknownString'` → null; `repeatRule==='weeklyOne'` with `dayOfWeek===null` → null; `repeatRule==='custom'` with `repeatType===undefined` → null.
   - **Plus legacy-row fallback**: `repeatRule==='custom', repeatType=2, repeatWeekdaysCsv===null` (pre-migration row) → `weeklyAll`/`everyNWeekAll`. Document that this is the intentional fallback for old data.
   - **Plus weekdays mapping**: `repeatRule==='weekdays', n=1` → `weeklyMulti` with `weekdays===[1,2,3,4,5]`; assert `formatCustomRepeatLabel` then renders the explicit Mon-Fri list, not "Ugentlig på alle dage".
3. **Playwright e2e** (1 test) in `r/calendar-ui-enhancements.spec.ts`: create a multi-day recurring event ("Mon, Wed, Fri, every 2 weeks, after 6 occurrences"), reload the calendar, click the event, verify the repeat dropdown shows `Hver 2. uge: mandag, onsdag og fredag`. Click `Tilpasset…` and verify the modal opens with the same checkboxes pre-checked and `Efter 6 forekomster` selected.

## Migration & release sequencing

Per CLAUDE.md and prior memory:

1. Edit `AreaRulePlanning` in base → run `dotnet ef migrations add ...` → commit base → push.
2. Tag base with new version → publish NuGet (per "Restore NuGet refs after devgetchanges.sh" memory: bump base NuGet version via tag→publish flow before plugin CI).
3. In plugin source: bump `Microting.EformBackendConfigurationBase` NuGet ref → update DTO + controller → commit plugin → push.
4. In host app (full dev mode): update frontend models + helper + modal init → run `devgetchanges.sh` from plugin source repo → commit plugin (frontend changes) → push.

The C# integration test gates step 3; the Playwright test gates step 4.

**Rollback.** Each layer is additive and backwards-compatible:
- New columns are nullable → migration is harmless on its own.
- Old frontend builds (deployed pre-this-change on customer instances) keep working — they just don't send `repeatWeekdaysCsv` and the backend treats absent as null.
- Old backend builds + new frontend → frontend reads `undefined`, behaves as today (no reconstruction).

If a bug surfaces in step 4, **fix forward** — don't try to revert step 1. The migration is forward-only; rolling back the column drop after rows have been written would lose data.

## Out of scope

- **Backfilling existing rows** with `RepeatWeekdaysCsv` from `DayOfWeek` — old rows are simply null and reconstruct as the v1 fallback (`weeklyAll`/`everyNWeekAll` for `repeatRule='custom'` with null CSV; `weeklyOne` for `repeatRule='weeklyOne'`). Documented in the migration commit and tested explicitly.
- **Modal UI for selecting yearly month independently** — `RepeatMonth` was dropped from v1 (review M2). Reconstruction uses `taskDate.getUTCMonth()`. Future ticket can add the column + UI together.
- **JSON-blob alternative** — explicitly rejected in favour of discrete columns (option C).

## Risks / open questions

1. **Backend recurrence expansion path** — see Step 0 of the Approach. The audit MUST happen before Layer 1. If branch 3 is taken (iterator hardcodes `DayOfWeek` and is too large to fix in this PR), multi-day-weekly UI must be disabled or feature-flagged in the same PR — otherwise users save data the backend silently drops on render.
2. **Legacy rows fallback** — reconstruction returns sensible defaults (not `null`) for tasks saved before this migration. Users whose pre-migration custom rule was multi-day will see it as "Ugentlig på alle dage" until they re-save. Acceptable for v1; explicit tests cover the fallback.
