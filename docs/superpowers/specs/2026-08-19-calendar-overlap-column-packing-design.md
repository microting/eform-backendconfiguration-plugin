# Calendar overlap layout: Google-Calendar-style column packing

**Date:** 2026-08-19
**Status:** Approved (design locked with product owner)
**Area:** BackendConfiguration plugin — Angular calendar time-grid (week/day view)

## Problem

In the calendar time-grid, when several tasks overlap in time, each task box is positioned side-by-side using a right-stepping "cascade" (the base task stays wide and left-aligned; later overlapping tasks are drawn narrower, offset to the right, and stacked on top via z-index). This cascade *look* is correct and desired — it matches Google Calendar and lets the user still read the base task on its left portion.

The layout is only correct when every task in an overlap group mutually overlaps (e.g. identical timeslots). For **partial overlaps** — where some tasks in a connected overlap cluster do not actually overlap each other — the current algorithm assigns too many columns and never reuses a freed column, so later tasks are shrunk into ever-thinner right-hand slivers.

### Concrete failure (reproduced live in the app as task ids 3748–3750, Svinerådgivgningen, 2026-08-19)
Three tasks: A 09:00–10:00, B 09:30–11:00, C 10:30–11:30. A and C do NOT overlap each other; both overlap B. Correct layout is **2 columns** (A and C share the left column; B on the right). The current code produces **3 columns**, crushing C into a ~33%-wide sliver on the right (observed ~44px wide, title clipped).

## Root cause

`CalendarLayoutService.computeLayout` (`eform-client/src/app/plugins/modules/backend-configuration-pn/modules/calendar/services/calendar-layout.service.ts:11`) runs two phases:

- **Phase A (correct)** — lines 20–35: cluster tasks into connected components via a start-time sweep. After sorting by `startHour` ascending, a task joins the current cluster if `ev.startHour < currentGroupEnd` (the running max-end of the cluster); otherwise it flushes the current cluster and starts a new one. `currentGroupEnd` is `Math.max`-accumulated across the cluster.
- **Phase B (buggy)** — lines 37–55: for a cluster of size `n`, it lays out each task purely by its INDEX `i` in the cluster:

  ```ts
  const cardWidthPct = 100 / n;
  group.forEach((ev, i) => {
    ev._left = i * cardWidthPct;        // = i * (100 / n)
    ev._width = 100 - ev._left;         // extend to the right edge
    ev._zIndex = 10 + i;
    ev._inGroup = n > 1;
  });
  ```

  It uses the cluster's *size* (`n`) as the column count and the task's *position in the list* (`i`) as its column, so it never detects that two tasks in the same cluster don't overlap and could share a column.

The visual formula (`_left`, `_width`, `_zIndex`) is fine. Only the inputs to it — which column a task is in, and how many columns the cluster really needs — are computed wrong.

## Desired behavior

Keep the cascade visual exactly. Replace the column math with proper **column packing**:
- Within each cluster (Phase A unchanged), assign each task to a column via first-fit packing: process tasks sorted by start ascending (tie-break: longer duration first); place each task in the FIRST existing column whose last-placed task ends at or before this task's start (i.e. does not overlap it); if none fits, open a new column.
- `columnIndex` = the packed column; `numCols` = the number of columns the cluster ended up needing (true maximum concurrency).
- Geometry is the SAME cascade formula, now fed correct inputs: `_left = columnIndex/numCols·100`, `_width = 100 − _left`, `_zIndex = 10 + columnIndex`.

Overlap test between two tasks: they overlap iff `a.start < b.end && b.start < a.end`. Touching (a.end == b.start) is NOT overlap.

### Key invariant (no regression)
For a cluster whose tasks all mutually overlap (e.g. identical timeslots), first-fit packing yields `columnIndex == list-index` and `numCols == cluster size`, so the new algorithm produces **byte-identical** output to the current one. The change is observable ONLY for partial overlaps. This preserves the same-timeslot rendering the product owner confirmed is already correct.

### Worked results
- Partial chain A 09:00–10:00 / B 09:30–11:00 / C 10:30–11:30 → 2 columns: A col0 (left0,w100), B col1 (left50,w50), C col0 (left0,w100). (Was: 3 cols, C left67/w33 sliver.)
- Google reference (Tjekke grise 09–14 base + Overbrusning 09:30–10, Faringsrunde 11:30–12, Faringsrunde og split 13–13:30, Div. registreringer 13:30–14:30) → 2 columns: base full-width underneath, every short task at left50/w50 stacked on top. (Was: 5 cols, widths 100/80/60/40/20.)

## The seam / change surface

Single pure function: `CalendarLayoutService.computeLayout(tasks: CalendarTaskModel[]): CalendarTaskLayoutModel[]`. Phase A (lines 20–35) stays; Phase B's index-based loop (lines 37–55) is replaced by the packing + cascade above. Downstream is untouched:

- The container calls `computeLayout` once per day array inside `rebuildLayout` (`.../modules/calendar/components/calendar-container/calendar-container.component.ts:443–445`), mapping it over the 7-element `tasksByDay`.
- `calendar-task-block` renders whatever `_left`/`_width`/`_zIndex` are produced via the `leftStyle` / `widthStyle` / `zIndexStyle` getters (`.../components/calendar-task-block/calendar-task-block.component.ts:70–83`); a raised card returns z-index `999` (line 82), everything else returns `_zIndex`.

No template, model, or component changes are required. (Optional, non-essential: a `_columnIndex`/`_columnCount` field could be added to `CalendarTaskLayoutModel` for debug/inspection, but it is not needed — `_left`/`_width` remain the contract the view reads.)

> Note: the `CalendarTaskLayoutModel` doc-comment (in `.../backend-configuration-pn/models/calendar/calendar-task.model.ts`, the field block starting at line 149) still describes an OLDER "overlap factor 1.8" formula (`_width = 100 * 1.8 / N`, `_left = i * (100 - _width) / (N - 1)`) that the running service code no longer implements. It is stale documentation only — it does not affect layout — but it should be corrected to describe the packed-column cascade while this work is in flight.

Existing behaviors to preserve: the `_inGroup` flag / click-to-raise (raised task gets z-index 999) must keep working; the leftmost/base task (whose `_width === 100`) must still be raisable. The packing must set `_inGroup` on the same "cluster size > 1" condition as today (Phase B line 53, `ev._inGroup = n > 1`).

## Testing (TDD; tests run in CI only, never locally)

Rewrite `calendar-layout.service.spec.ts` (`.../modules/calendar/services/calendar-layout.service.spec.ts`). The current spec does NOT yet contain a test for the true partial-overlap bug: its two "partial" cases ("partially overlapping group…" at lines 86–100, and "task whose start is at the end of a prior conflict group…" at lines 116–126) only exercise a *mutually-overlapping pair* plus a *disjoint third*, which BOTH the old and new algorithms render identically — so they are not the buggy case and stay valid. The rewrite therefore ADDS the failing partial-chain test and preserves the mutual-overlap / same-timeslot assertions (the no-regression invariant) unchanged. Add/keep:
1. Empty, single, and fully-disjoint tasks (each full width, own cluster).
2. **No-regression:** N identical-timeslot tasks (N=2,3,4) — assert the exact same cascade output the current code produces (this is the invariant; it must not change). The existing cascade tests at spec lines 55–84 and 128–142 already pin N=2/3/4 and stay as-is.
3. **The bug:** the A/B/C partial chain — assert 2 columns: A {left0,w100}, B {left50,w50}, C {left0,w100}. (This test must fail against the current code and pass after the fix — the red→green proof.)
4. Google-reference cluster (one long base + several short non-mutually-overlapping tasks) — assert numCols=2 and every short task at left50/w50.
5. First-fit column reuse: a task that starts exactly when a column's previous task ends (touching) may reuse that column (touching ≠ overlap).
6. Tie-break determinism: two tasks with the same start but different durations — assert stable, documented ordering (longer first).

## Out of scope
- No change to clustering (Phase A), the data model, event creation, recurrence, or any backend code. Pure frontend layout math.
- No switch to non-overlapping "clean tiling" — the overlap cascade is intentional and stays.

## Risks
- The new partial-overlap assertions must be written to the corrected geometry; reviewers should confirm they match the packed-column cascade and that the same-timeslot invariant test genuinely pins the no-regression guarantee.
- Tie-break ordering affects which task lands in col0 when two share a start; the spec fixes "longer first" for determinism.
