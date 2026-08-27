# Calendar Overlap Column Packing Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the index-based column math in `CalendarLayoutService.computeLayout` Phase B with Google-Calendar-style first-fit column packing, so partial-overlap clusters reuse freed columns instead of crushing later tasks into ever-thinner right-hand slivers — while keeping the cascade visual and same-timeslot output byte-identical.

**Architecture:** `computeLayout` is a single pure function. Phase A (connected-component clustering by a start-time sweep) is unchanged. Phase B is rewritten: within each cluster, tasks are sorted by start ascending (tie-break: longer duration first) and assigned to columns by first-fit packing (a task reuses the first column whose last-placed task ends at or before this task's start; otherwise a new column opens). The SAME cascade geometry formula (`_left = columnIndex/numCols·100`, `_width = 100 − _left`, `_zIndex = 10 + columnIndex`, `_inGroup = clusterSize > 1`) is then fed the corrected `columnIndex`/`numCols` inputs. No template, model, or component changes are required beyond a stale doc-comment fix.

**Tech Stack:** Angular (TypeScript) plugin frontend; Jest 30 for unit tests; the tests run in the `eform-angular-frontend` host assembly (BackendConfiguration plugin copied in) under CI.

## Global Constraints

- **Tests run in CI ONLY, NEVER locally.** Do not run `jest`/`ng test` on this machine. Local-allowed actions are limited to reading, type/name consistency review, and (where a host toolchain exists) build/analyze/codegen — never the test suite.
- **Authoritative red→green is CI**, specifically the plugin's `test-angular-client` job (Jest 30, host-frontend assembly). The exact CI command is: `cd eform-angular-frontend/eform-client && npx jest --ci --maxWorkers=2 "src/app/plugins/modules/backend-configuration-pn/"`. Every "expect fail/pass" below is a **CI expectation**, proven on the PR — not a local run.
- **Jest idiom only** in the spec: use `expect(...).toBe(...)`, `.toEqual(...)`, `.toBeCloseTo(...)`, `.toHaveLength(...)`. NO jasmine.* APIs, NO `toBeTrue()`/`toBeFalse()` (use `.toBe(true)`/`.toBe(false)`), NO global `fail()`.
- **Follow the existing spec file's style** exactly: the `makeTask(id, startHour, dur)` helper, one `describe('CalendarLayoutService', ...)` block, `beforeEach` constructing `new CalendarLayoutService()`, `it(...)` cases with inline comments explaining the geometry.
- **Design is locked** — implement the approved spec `docs/superpowers/specs/2026-08-19-calendar-overlap-column-packing-design.md` exactly. No clean-tiling, no data-model changes, no Phase A changes, no backend changes.
- **Overlap test:** two tasks overlap iff `a.start < b.end && b.start < a.end`. Touching (`a.end === b.start`) is NOT overlap and MAY reuse a column.
- **No-regression invariant:** for a cluster whose tasks all mutually overlap with identical start AND duration, first-fit yields `columnIndex === list-index` and `numCols === clusterSize`, producing byte-identical output to the current code. This MUST be preserved.
- **Commits:** standard commits, one per task, each ending test-first (test added before or with the implementation it guards). Do NOT push, do NOT open a PR, do NOT amend, do NOT touch git config.

---

## File Structure

- `eform-client/src/app/plugins/modules/backend-configuration-pn/modules/calendar/services/calendar-layout.service.ts` — the pure layout function. Phase A untouched; Phase B replaced. (Task 2)
- `eform-client/src/app/plugins/modules/backend-configuration-pn/modules/calendar/services/calendar-layout.service.spec.ts` — Jest unit tests. New failing tests added (Task 1), guard/regression tests added (Task 3). Existing valid tests preserved unchanged.
- `eform-client/src/app/plugins/modules/backend-configuration-pn/models/calendar/calendar-task.model.ts` — stale doc-comment on `CalendarTaskLayoutModel` corrected (Task 4). No structural/type change.

---

### Task 1: Add the failing partial-chain and Google-reference tests (RED)

**Files:**
- Test: `eform-client/src/app/plugins/modules/backend-configuration-pn/modules/calendar/services/calendar-layout.service.spec.ts`

**Interfaces:**
- Consumes: `CalendarLayoutService.computeLayout(tasks: CalendarTaskModel[]): CalendarTaskLayoutModel[]` (existing); the existing `makeTask(id: number, startHour: number, dur: number): CalendarTaskModel` helper in the spec.
- Produces: two new `it(...)` cases that FAIL against the current index-based Phase B and will PASS after Task 2.

**Why these fail today:** the current Phase B assigns `_left = i * (100/n)` using cluster size `n` and list index `i`. For the A/B/C partial chain it produces `n = 3` columns (C at `_left ≈ 66.67`, `_width ≈ 33.33`), not the correct 2 columns (C reusing A's column at `_left 0`, `_width 100`). For the Google-reference cluster it produces 5 columns (widths 100/80/60/40/20), not 2.

- [ ] **Step 1: Write the two failing tests**

Insert these two `it(...)` blocks inside the existing `describe('CalendarLayoutService', () => { ... })` block, immediately AFTER the existing `it('partially overlapping group: two tasks overlap, third is separate', ...)` case (which stays unchanged). Do not modify any existing test.

```ts
  it('partial chain A/B/C: freed column is reused (2 columns, not 3)', () => {
    // A 09:00–10:00, B 09:30–11:00, C 10:30–11:30.
    // A and C do NOT overlap each other; both overlap B → true concurrency is 2.
    // First-fit packs: A→col0, B→col1, C reuses col0 (A ended 10:00 ≤ C start 10:30).
    // numCols = 2 → _left = columnIndex/2*100, _width = 100 - _left.
    const result = service.computeLayout([
      makeTask(1, 9, 1),     // A 09:00–10:00
      makeTask(2, 9.5, 1.5), // B 09:30–11:00
      makeTask(3, 10.5, 1),  // C 10:30–11:30
    ]);
    const a = result.find(t => t.id === 1)!;
    const b = result.find(t => t.id === 2)!;
    const c = result.find(t => t.id === 3)!;
    // A in col0: full width, left 0.
    expect(a._left).toBe(0);
    expect(a._width).toBe(100);
    // B in col1 of 2: left 50, extends to the right edge → width 50.
    expect(b._left).toBe(50);
    expect(b._width).toBe(50);
    // C reuses col0 (A's freed column): left 0, full width — NOT a 33% sliver.
    expect(c._left).toBe(0);
    expect(c._width).toBe(100);
    // z-index tracks columnIndex: col0 → 10, col1 → 11.
    expect(a._zIndex).toBe(10);
    expect(c._zIndex).toBe(10);
    expect(b._zIndex).toBe(11);
    // All three share a cluster → all flagged in-group for click-to-raise.
    expect(result.every(t => t._inGroup === true)).toBe(true);
  });

  it('Google-reference cluster: one long base + short non-mutually-overlapping tasks → 2 columns', () => {
    // Tjekke grise 09:00–14:00 (base) plus four short tasks that each overlap the
    // base but not one another. First-fit: base→col0, every short task packs into
    // col1 (each starts at/after the previous short task ends). numCols = 2.
    const result = service.computeLayout([
      makeTask(1, 9, 5),      // base            09:00–14:00
      makeTask(2, 9.5, 0.5),  // Overbrusning    09:30–10:00
      makeTask(3, 11.5, 0.5), // Faringsrunde    11:30–12:00
      makeTask(4, 13, 0.5),   // Faring og split 13:00–13:30
      makeTask(5, 13.5, 1),   // Div. registr.   13:30–14:30
    ]);
    const base = result.find(t => t.id === 1)!;
    // Base sits full-width underneath in col0.
    expect(base._left).toBe(0);
    expect(base._width).toBe(100);
    expect(base._zIndex).toBe(10);
    // Every short task packs into col1 → left 50, width 50, z 11.
    const shorts = result.filter(t => t.id !== 1);
    expect(shorts).toHaveLength(4);
    expect(shorts.every(t => t._left === 50)).toBe(true);
    expect(shorts.every(t => t._width === 50)).toBe(true);
    expect(shorts.every(t => t._zIndex === 11)).toBe(true);
  });
```

- [ ] **Step 2: Confirm the tests fail (CI expectation)**

Do NOT run Jest locally (Global Constraints). The authoritative check is the `test-angular-client` CI job on the PR:
`cd eform-angular-frontend/eform-client && npx jest --ci --maxWorkers=2 "src/app/plugins/modules/backend-configuration-pn/modules/calendar/services/calendar-layout.service.spec.ts"`
Expected CI outcome: **FAIL (red)** — the two new cases fail. `partial chain A/B/C…` fails because current code gives C `_left ≈ 66.667`/`_width ≈ 33.333`; `Google-reference cluster…` fails because current code gives 5 columns (`_left` values 0/20/40/60/80). All pre-existing tests still pass.

- [ ] **Step 3: Commit the failing tests**

```bash
git add eform-client/src/app/plugins/modules/backend-configuration-pn/modules/calendar/services/calendar-layout.service.spec.ts
git commit -m "test: add failing partial-chain and Google-reference overlap tests"
```

---

### Task 2: Replace Phase B with first-fit column packing (GREEN)

**Files:**
- Modify: `eform-client/src/app/plugins/modules/backend-configuration-pn/modules/calendar/services/calendar-layout.service.ts:37-55`

**Interfaces:**
- Consumes: the Phase A output — `groups: CalendarTaskLayoutModel[][]`, each inner array a cluster of `CalendarTaskLayoutModel` objects already globally sorted by `startHour` ascending (stable on ties). Each object has `startHour: number`, `duration: number`, and the pre-seeded layout fields `_left`, `_width`, `_zIndex`, `_inGroup`.
- Produces: no new exported symbol. Mutates each event's `_left`/`_width`/`_zIndex`/`_inGroup` in place. Signature of `computeLayout` is unchanged: `(tasks: CalendarTaskModel[]) => CalendarTaskLayoutModel[]`.

- [ ] **Step 1: Replace the Phase B `groups.forEach` block**

Open the service. Phase A (lines 11–35: the early return, the sorted `events` map, and the `events.forEach` sweep that builds `groups`) stays EXACTLY as-is. Replace the current Phase B block — the `groups.forEach(group => { ... });` starting at line 37 through line 55, up to but not including `return events;` — with the following. The `return events;` line at the end of the method is unchanged.

```ts
    // Phase B: within each cluster, pack tasks into columns via first-fit, then
    // feed the SAME cascade geometry the corrected inputs. A cluster whose tasks
    // all mutually overlap (identical start+duration) yields columnIndex === list
    // index and numCols === cluster size, i.e. byte-identical to the old output.
    groups.forEach(group => {
      // Sort by start ascending, tie-break longer duration first (deterministic:
      // the longer task lands in the earlier column when two share a start).
      const ordered = group
        .slice()
        .sort((a, b) =>
          a.startHour - b.startHour || (b.duration || 1) - (a.duration || 1));

      // First-fit column packing. Each column holds its placed tasks in start
      // order; a task reuses the first column whose LAST task ends at or before
      // this task's start (touching counts as free — not an overlap). Because
      // tasks are start-sorted and only appended when they clear the last task,
      // each column's ends are monotonically non-decreasing, so the last task
      // alone determines the column's availability.
      const columns: CalendarTaskLayoutModel[][] = [];
      ordered.forEach(ev => {
        const evStart = ev.startHour;
        let placed = false;
        for (const col of columns) {
          const last = col[col.length - 1];
          const lastEnd = last.startHour + (last.duration || 1);
          if (lastEnd <= evStart) {
            col.push(ev);
            placed = true;
            break;
          }
        }
        if (!placed) {
          columns.push([ev]);
        }
      });

      const numCols = columns.length;
      // Flag multi-card clusters so click-to-raise works even for the leftmost
      // card (whose _width === 100 is indistinguishable from a solo card).
      // Keyed on cluster size, identical to the old `n > 1`.
      const inGroup = group.length > 1;
      columns.forEach((col, columnIndex) => {
        const left = (columnIndex / numCols) * 100;
        col.forEach(ev => {
          ev._left = left;
          ev._width = 100 - left; // extend to the right edge, behind cards in front
          ev._zIndex = 10 + columnIndex;
          ev._inGroup = inGroup;
        });
      });
    });
```

- [ ] **Step 2: Confirm the Task 1 tests now pass (CI expectation)**

Do NOT run Jest locally. Authoritative check is the `test-angular-client` CI job on the PR (command as in Task 1 Step 2).
Expected CI outcome: **PASS (green)** — both Task 1 cases pass AND every pre-existing test still passes (the same-start+same-duration cascade cases are byte-identical under packing). Trace to self-verify by hand before committing:
- **A/B/C:** ordered = A(9,1), B(9.5,1.5), C(10.5,1). A→col0. B: col0 last A ends 10 > 9.5 → new col1. C: col0 last A ends 10 ≤ 10.5 → reuse col0. numCols 2. A/C col0 (left 0, w 100, z 10), B col1 (left 50, w 50, z 11). ✓
- **Google reference:** base→col0. Overbrusning(9.5)→col1. Faringsrunde(11.5): col1 last ends 10 ≤ 11.5 → reuse col1. Faring-split(13): col1 last ends 12 ≤ 13 → reuse. Div(13.5): col1 last ends 13.5 ≤ 13.5 (touching) → reuse. numCols 2. base col0, all shorts col1 (left 50, w 50, z 11). ✓
- **3 identical (9,2)×3:** ordered = same (stable). t0→col0; t1: col0 last ends 11 > 9 → col1; t2: both busy → col2. numCols 3 → lefts 0/33.3/66.7, widths 100/66.7/33.3, z 10/11/12 — identical to old. ✓

- [ ] **Step 3: (Local-allowed) type/consistency review**

Jest cannot run locally. Locally allowed: re-read the edited method top-to-bottom and confirm Phase A is untouched, `return events;` is intact, no stray references to the removed `n`/`cardWidthPct`, and the mutated fields match `CalendarTaskLayoutModel` (`_left`, `_width`, `_zIndex`, `_inGroup`). If a host TypeScript toolchain is available for the assembled frontend, a `tsc --noEmit` typecheck is permitted; the jest suite is not.

- [ ] **Step 4: Commit the implementation**

```bash
git add eform-client/src/app/plugins/modules/backend-configuration-pn/modules/calendar/services/calendar-layout.service.ts
git commit -m "fix: pack calendar overlap clusters into first-fit columns"
```

---

### Task 3: No-regression, column-reuse, and tie-break guard tests

**Files:**
- Test: `eform-client/src/app/plugins/modules/backend-configuration-pn/modules/calendar/services/calendar-layout.service.spec.ts`

**Interfaces:**
- Consumes: `CalendarLayoutService.computeLayout` (post-Task-2 behavior); the `makeTask` helper.
- Produces: three new `it(...)` cases that pass against the Task 2 implementation, pinning (a) the N=2/3/4 identical-timeslot invariant explicitly, (b) touching-column reuse within a single cluster, and (c) tie-break determinism (longer duration first).

**Note on pre-existing coverage:** the existing spec already pins the identical-timeslot cascade at N=2 (lines 55–68), N=3 (lines 70–84), and N=4 (lines 128–142), and the disjoint/single/empty/touching-separates-clusters cases (lines 31–53, 86–126). Those stay UNCHANGED and continue to pass — Task 2's invariant guarantees it. This task adds a single consolidated invariant assertion plus the two behaviors the old spec never exercised.

- [ ] **Step 1: Write the three guard tests**

Append these three `it(...)` blocks inside the `describe('CalendarLayoutService', ...)` block, after the last existing case (`it('rightmost cascaded card sits on top of its conflict group by default', ...)`). Do not modify existing cases.

```ts
  it('no-regression invariant: identical timeslots produce the old cascade for N=2,3,4', () => {
    // For N mutually-overlapping identical tasks, first-fit assigns
    // columnIndex === list index and numCols === N → byte-identical to the
    // pre-packing output. Assert the exact geometry for N = 2, 3, 4.
    for (const n of [2, 3, 4]) {
      const tasks = Array.from({length: n}, (_, i) => makeTask(i + 1, 9, 2));
      const result = service.computeLayout(tasks);
      expect(result).toHaveLength(n);
      result.forEach((t, i) => {
        expect(t._left).toBeCloseTo((i / n) * 100, 10);
        expect(t._width).toBeCloseTo(100 - (i / n) * 100, 10);
        expect(t._zIndex).toBe(10 + i);
        expect(t._inGroup).toBe(true);
      });
    }
  });

  it('touching tasks in one cluster reuse the same column', () => {
    // A 09:00–10:00 and B 10:00–11:00 touch (A.end === B.start → not overlap),
    // but C 09:30–10:30 bridges them into a single cluster. First-fit: A→col0,
    // C→col1 (overlaps A), B reuses col0 (A ended 10:00 ≤ B start 10:00, touching).
    const result = service.computeLayout([
      makeTask(1, 9, 1),    // A 09:00–10:00
      makeTask(2, 9.5, 1),  // C 09:30–10:30 (bridge)
      makeTask(3, 10, 1),   // B 10:00–11:00
    ]);
    const a = result.find(t => t.id === 1)!;
    const c = result.find(t => t.id === 2)!;
    const b = result.find(t => t.id === 3)!;
    // A and B share col0 (touching reuse) → same left/width.
    expect(a._left).toBe(0);
    expect(a._width).toBe(100);
    expect(b._left).toBe(0);
    expect(b._width).toBe(100);
    // C is the concurrent one in col1 of 2.
    expect(c._left).toBe(50);
    expect(c._width).toBe(50);
  });

  it('tie-break: when two tasks share a start, the longer one takes the earlier column', () => {
    // Same start 09:00, durations 2h and 1h. They mutually overlap → 2 columns.
    // Tie-break "longer duration first" is deterministic regardless of input
    // order: the 2h task lands in col0 (left 0), the 1h task in col1 (left 50).
    const longerFirst = service.computeLayout([makeTask(1, 9, 2), makeTask(2, 9, 1)]);
    const shorterFirst = service.computeLayout([makeTask(1, 9, 1), makeTask(2, 9, 2)]);
    for (const result of [longerFirst, shorterFirst]) {
      const longer = result.find(t => t.duration === 2)!;
      const shorter = result.find(t => t.duration === 1)!;
      expect(longer._left).toBe(0);
      expect(longer._width).toBe(100);
      expect(shorter._left).toBe(50);
      expect(shorter._width).toBe(50);
    }
  });
```

- [ ] **Step 2: Confirm the guard tests pass (CI expectation)**

Do NOT run Jest locally. Authoritative check is the `test-angular-client` CI job on the PR (command as in Task 1 Step 2).
Expected CI outcome: **PASS (green)** — all three new cases pass against the Task 2 implementation, and the full pre-existing suite still passes. Self-verify the tie-break trace: for both input orders the cluster `ordered` = [dur2, dur1] (longer first); dur2→col0, dur1→col1 (overlaps) → dur2 left 0/w 100, dur1 left 50/w 50. ✓

- [ ] **Step 3: Commit the guard tests**

```bash
git add eform-client/src/app/plugins/modules/backend-configuration-pn/modules/calendar/services/calendar-layout.service.spec.ts
git commit -m "test: pin identical-timeslot invariant, column reuse, tie-break"
```

---

### Task 4: Fix the stale `CalendarTaskLayoutModel` doc-comment

**Files:**
- Modify: `eform-client/src/app/plugins/modules/backend-configuration-pn/models/calendar/calendar-task.model.ts:149-163`

**Interfaces:**
- Consumes: nothing. Documentation-only change.
- Produces: no code/type/behavior change; `CalendarTaskLayoutModel`'s fields (`_left`, `_width`, `_zIndex`, `_inGroup?`) are unchanged.

**Why:** the current doc-comment describes an obsolete "overlap factor 1.8" formula (`_width = 100 * 1.8 / N`, `_left = i * (100 - _width) / (N - 1)`) the running code never implemented and which is now doubly wrong after Task 2. Correct it to describe the packed-column cascade.

- [ ] **Step 1: Replace the doc-comment**

Replace the leading comment block of `CalendarTaskLayoutModel` (the lines from `// Google-Calendar-style equal-divide-with-overlap layout.` through `// For N == 1, _left=0, _width=100, _zIndex=10.`) with:

```ts
  // Google-Calendar-style packed-column cascade layout, computed by
  // CalendarLayoutService.computeLayout. Tasks in a time-overlap cluster are
  // packed into columns via first-fit (a task reuses the first column whose last
  // task ends at or before it starts; otherwise a new column opens). With
  // `numCols` columns and a task in `columnIndex`:
  //   _left   = columnIndex / numCols * 100
  //   _width  = 100 - _left   (each card extends to the right edge, so cards
  //                            behind run underneath the ones in front)
  //   _zIndex = 10 + columnIndex   (later columns layer on top)
  // For a solo task (numCols === 1): _left = 0, _width = 100, _zIndex = 10.
```

- [ ] **Step 2: (Local-allowed) confirm comment-only change**

Jest cannot run locally. Locally allowed: `git diff` the model file and confirm ONLY the comment lines changed — the `_left`/`_width`/`_zIndex`/`_inGroup?` declarations and every other interface in the file are untouched. No CI behavior change is expected (comment-only).

- [ ] **Step 3: Commit the doc fix**

```bash
git add eform-client/src/app/plugins/modules/backend-configuration-pn/models/calendar/calendar-task.model.ts
git commit -m "docs: correct CalendarTaskLayoutModel layout comment to packed-column cascade"
```

---

## Self-Review

**1. Spec coverage** — every spec requirement maps to a task:

| Spec requirement | Task |
| --- | --- |
| Keep Phase A (start-sweep clustering) unchanged | Task 2 (Phase A explicitly untouched) |
| First-fit packing: sort by start asc, tie-break longer first | Task 2 Step 1 (`ordered` sort) |
| Reuse first column whose last task ends ≤ start; else new column | Task 2 Step 1 (packing loop) |
| `columnIndex`/`numCols` drive geometry `_left`/`_width`/`_zIndex` | Task 2 Step 1 (geometry loop) |
| `_inGroup` keyed on cluster size > 1 (same as old `n > 1`) | Task 2 Step 1 (`inGroup = group.length > 1`) |
| Overlap test `a.start < b.end && b.start < a.end`; touching not overlap | Task 2 packing (`lastEnd <= evStart`); Task 3 touching-reuse test |
| No-regression invariant (identical timeslots byte-identical, N=2,3,4) | Task 2 Step 2 trace; existing tests + Task 3 invariant test |
| The bug: A/B/C partial chain → 2 cols, C {left0,w100} | Task 1 test 1 (red→green) |
| Google-reference cluster → numCols 2, shorts at left50/w50 | Task 1 test 2 (red→green) |
| First-fit touching column reuse test | Task 3 touching-reuse test |
| Tie-break determinism (longer first) test | Task 3 tie-break test |
| Empty/single/disjoint preserved | Existing tests (unchanged), noted in Task 3 |
| Stale "overlap factor 1.8" doc-comment corrected | Task 4 |
| No template/model/component structural change; pure layout math | All tasks (only comment touched in model) |

No gaps.

**2. Placeholder scan** — no TBD/TODO/"handle edge cases"/"similar to above". Every code step shows complete TypeScript (full replacement Phase B body, full `it(...)` bodies, full replacement doc-comment). No pseudocode.

**3. Type consistency** — `computeLayout(tasks: CalendarTaskModel[]): CalendarTaskLayoutModel[]` used consistently; mutated fields `_left`/`_width`/`_zIndex`/`_inGroup` match `CalendarTaskLayoutModel` exactly; `makeTask(id, startHour, dur)` signature matches the existing spec helper; `columns: CalendarTaskLayoutModel[][]`, `ordered`, `numCols`, `inGroup` are internally consistent within Task 2. The CI command string is identical across all "expect fail/pass" steps.
