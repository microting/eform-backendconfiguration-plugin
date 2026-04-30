# Edit-mode meta reconstruction for custom calendar repeat rules

## Context

The calendar's task-edit modal cannot reconstruct a `CalendarRepeatMeta` from a backend-loaded task. When the user re-opens an existing task whose `repeatRule === 'custom'`, the modal can't tell what the rule was — only that it's "some custom thing" — so the dropdown either pops the custom-config modal automatically or shows defaults. The user has to reconfigure from scratch.

Two root causes:

1. **Multi-day weekly is lost on save.** `AreaRulePlanning` only persists `DayOfWeek` (a single int). For `weeklyMulti`/`everyNWeekMulti` patterns ("Mon, Wed, Fri"), the day list is dropped. Loading them back yields at best a single-day rule.
2. **Even what is persisted is omitted from the response DTO.** `CalendarTaskResponseModel` returns `repeatType` and `repeatEvery` but NOT `RepeatEndMode`, `RepeatOccurrences`, `RepeatUntilDate`, or `DayOfMonth`. So the frontend sees only a fragment of the rule.

The chosen fix (option C — discrete columns, picked over a JSON blob) adds two new columns to `AreaRulePlanning`, extends the response DTO, and adds a frontend reconstruction helper. This preserves the relational schema (no opaque blobs) and lets the backend recurrence calculator continue using discrete fields.

## Approach

Three layers in two repos. Sequence: base → backend plugin → frontend. Each step blocks the next.

### Layer 1 — Base (`eform-backendconfiguration-base`)

New properties on `AreaRulePlanning`:

```csharp
public string? RepeatWeekdaysCsv { get; set; }   // "1,3,5" — JS getDay() values
public int? RepeatMonth { get; set; }             // 0..11, null for non-yearly
```

Both nullable. `RepeatWeekdaysCsv` only set for `weeklyMulti`/`everyNWeekMulti`. `RepeatMonth` only set for `yearlyOne`/`everyNYear` (redundant with task start-date month at v1; kept so a future modal can decouple "rule month" from "first occurrence month" without another migration).

**EF migration generated via the existing factory** — never hand-written:

```bash
cd Microting.EformBackendConfigurationBase
dotnet ef migrations add AddRepeatWeekdaysAndMonthToAreaRulePlanning \
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
public int? RepeatMonth { get; set; }
public string? RepeatRule { get; set; }
```

`CalendarController.GetTasksForWeek` mapper populates all fields from `AreaRulePlanning`.

**Create/update endpoints** (`CalendarController.CreateTask` / `UpdateTask`) accept the corresponding fields on the request models and write them to the entity. No change to recurrence-expansion code in v1 (see Risks).

### Layer 3 — Frontend (`eform-angular-frontend` host app)

**Models**

- `CalendarTaskModel` (response shape) — add the new fields above.
- `CalendarTaskCreateModel` / `CalendarTaskUpdateModel` (request shapes) — add `repeatWeekdaysCsv?: string`, `repeatMonth?: number`.

**Save flow** (`task-create-edit-modal.component.ts:onSave`)

When `customRepeatMeta` exists, populate the new request fields:

```ts
repeatWeekdaysCsv: this.customRepeatMeta?.weekdays?.join(',') ?? null,
repeatMonth: this.customRepeatMeta?.month ?? null,
```

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

switch (r) {
  case 'daily':
    return {kind: n === 1 ? 'daily' : 'everyNd', n, endMode, afterCount, untilTs};
  case 'weeklyOne':
    return {kind: n === 1 ? 'weeklyOne' : 'everyNWeekOne', n,
            weekday: task.dayOfWeek!, endMode, afterCount, untilTs};
  case 'weeklyAll':
  case 'weekdays':
    return {kind: n === 1 ? 'weeklyAll' : 'everyNWeekAll', n, endMode, afterCount, untilTs};
  case 'monthlyDom':
    return {kind: n === 1 ? 'monthlyDom' : 'everyNMonthDom', n,
            dom: task.dayOfMonth!, endMode, afterCount, untilTs};
  case 'yearlyOne':
    return {kind: n === 1 ? 'yearlyOne' : 'everyNYear', n,
            dom: task.dayOfMonth!,
            month: task.repeatMonth ?? new Date(task.taskDate).getMonth(),
            endMode, afterCount, untilTs};
  case 'custom': {
    const days = (task.repeatWeekdaysCsv ?? '').split(',').filter(Boolean).map(Number);
    switch (task.repeatType) {
      case 1: return {kind: n === 1 ? 'daily' : 'everyNd', n, endMode, afterCount, untilTs};
      case 2:
        if (days.length === 0) return {kind: n === 1 ? 'weeklyAll' : 'everyNWeekAll', n, endMode, afterCount, untilTs};
        if (days.length === 1) return {kind: n === 1 ? 'weeklyOne' : 'everyNWeekOne', n, weekday: days[0], endMode, afterCount, untilTs};
        return {kind: n === 1 ? 'weeklyMulti' : 'everyNWeekMulti', n, weekdays: days, endMode, afterCount, untilTs};
      case 3: return {kind: n === 1 ? 'monthlyDom' : 'everyNMonthDom', n, dom: task.dayOfMonth!, endMode, afterCount, untilTs};
      case 4: return {kind: n === 1 ? 'yearlyOne' : 'everyNYear', n,
                      dom: task.dayOfMonth!,
                      month: task.repeatMonth ?? new Date(task.taskDate).getMonth(),
                      endMode, afterCount, untilTs};
      default: return null;
    }
  }
  default: return null;
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
   - Create a task via `CreateTask` with `RepeatType=6, RepeatEvery=2, RepeatWeekdaysCsv="1,3,5", RepeatEndMode=1, RepeatOccurrences=10`.
   - Call `GetTasksForWeek` for a week containing the task.
   - Assert all those fields round-trip exactly.
   - Use the existing Testcontainers MariaDB pattern.
2. **Frontend Karma unit test** in `calendar-repeat.service.spec.ts`: round-trip every `kind` — `buildMetaFromCustomConfig` → save-payload-shape → `reconstructMetaFromTask(fakeTask)` → equality check. Covers all 12 kinds plus the three end modes.
3. **Playwright e2e** (1 test) in `r/calendar-ui-enhancements.spec.ts`: create a multi-day recurring event ("Mon, Wed, Fri, every 2 weeks, after 6 occurrences"), reload the calendar, click the event, verify the repeat dropdown shows `Hver 2. uge: mandag, onsdag og fredag`. Click `Tilpasset…` and verify the modal opens with the same checkboxes pre-checked and `Efter 6 forekomster` selected.

## Migration & release sequencing

Per CLAUDE.md and prior memory:

1. Edit `AreaRulePlanning` in base → run `dotnet ef migrations add ...` → commit base → push.
2. Tag base with new version → publish NuGet (per "Restore NuGet refs after devgetchanges.sh" memory: bump base NuGet version via tag→publish flow before plugin CI).
3. In plugin source: bump `Microting.EformBackendConfigurationBase` NuGet ref → update DTO + controller → commit plugin → push.
4. In host app (full dev mode): update frontend models + helper + modal init → run `devgetchanges.sh` from plugin source repo → commit plugin (frontend changes) → push.

The C# integration test gates step 3; the Playwright test gates step 4.

## Out of scope

- **Recurrence-expansion logic in the backend** — currently uses single-int `DayOfWeek` for weekly expansion. If multi-day rules need to actually expand to multiple weekly instances, that's a separate piece of work. v1 only persists & echoes the data; the existing expansion remains single-day. (Worth confirming via a quick check during implementation: does the backend already iterate weekdays for `weeklyMulti` somehow, or is it just emitting the master?)
- **Backfilling existing rows** with `RepeatWeekdaysCsv` from `DayOfWeek` — old rows are simply null and reconstruct as `weeklyOne` (single-day) on edit. Acceptable; document in the migration commit message.
- **Modal UI for selecting `RepeatMonth` independently** — column added but no UI hook in v1. Future ticket if users want yearly rules on a different month than the first occurrence.
- **JSON-blob alternative** — explicitly rejected in favour of discrete columns (option C).

## Risks / open questions

1. **Backend recurrence expansion path may need updates** for multi-day weekly to actually generate multiple instances. Quick read of the existing iterator should reveal whether it already supports weekday lists or hardcodes the single `DayOfWeek`. If hardcoded, this is a separate (larger) ticket.
2. **Existing rows with `repeatRule='custom'` and missing context fields** — reconstruction returns `null` and the modal falls back to the unchanged `setValue(task.repeatRule ?? 'none')` behaviour, which auto-pops the custom-config modal. Behaviour for legacy rows is the same as today; new rows get the better UX.
3. **`RepeatMonth` redundancy** — column added but UX-wise indistinguishable from `task.taskDate.getMonth()` until/unless the modal exposes month selection. Could be deferred to a later migration if you'd rather minimize the schema churn now.
