# Calendar Modal — Sticky Close + Save, Scrolling Body

## Problem

In the calendar task create/edit modal (`task-create-edit-modal.component`), the
close X in the top-right corner and the Save button at the bottom are **inside**
the scrollable area. `.gcal-popover-card` has `max-height: 90vh; overflow-y: auto`,
which makes the close row, form body, and action row scroll together as one
block. As soon as the user scrolls, the close X disappears off the top and the
Save button disappears off the bottom.

## Goal

Keep the close X and Save button visible at all times; only the form content
between them should scroll.

## Design

Restructure `.gcal-popover-card` into a 3-zone flex column:

```
.gcal-popover-card (flex column, max-height: 90vh, no overflow)
├── .gcal-close-row        ← fixed at top, flex-shrink: 0
├── .gcal-scroll-body      ← NEW wrapper, flex: 1, overflow-y: auto
│   └── .gcal-modal        ← existing form content (unchanged)
└── .gcal-actions          ← fixed at bottom (when !isReadonly), flex-shrink: 0
```

The card stops scrolling; only `.gcal-scroll-body` scrolls.

### Template change

File: `modules/calendar/modals/task-create-edit-modal/task-create-edit-modal.component.html`

Wrap the `.gcal-modal` block with a new `<div class="gcal-scroll-body">`:

- Open the new wrapper before `<div class="gcal-modal">` (currently line 8).
- Close the new wrapper after the `.gcal-modal`'s closing `</div>` (currently line 243), before `<div class="gcal-actions" *ngIf="!isReadonly">` (currently line 245).

`.gcal-actions` stays a sibling of the new wrapper, still inside
`.gcal-popover-card`.

### SCSS change

File: `modules/calendar/modals/task-create-edit-modal/task-create-edit-modal.component.scss`

- `.gcal-popover-card`:
  - Remove `overflow-y: auto`.
  - Add `display: flex; flex-direction: column;`.
  - Keep `max-height: 90vh`, `background`, `border-radius`, `box-shadow`.
- `.gcal-close-row`: add `flex-shrink: 0`.
- `.gcal-scroll-body` (new): `flex: 1 1 auto; overflow-y: auto; min-height: 0;`.
  - `min-height: 0` is required so a flex child can actually shrink below its
    intrinsic content size and scroll — without it, the body won't scroll.
- `.gcal-actions`: add `flex-shrink: 0`.

### Non-popover usage

`.gcal-popover-card` is only applied when the component is rendered via the CDK
Overlay popover path (`[class.gcal-popover-card]="usePopoverMode"`). In the
regular MatDialog code path the wrapper class is absent, so nothing here
changes MatDialog behavior.

## Out of scope

- TypeScript changes — none needed.
- New translation keys — none.
- Test changes — no new e2e; existing shard `l` exercises the create flow.
- Restyling of the close button or Save button themselves.

## Verification

1. Open the create/edit event modal in popover mode on a short viewport.
2. Scroll the modal body — close X stays pinned top-right, Save button stays
   pinned bottom-right.
3. Resize the window so the content fits without scrolling — no visible change
   (body just doesn't scroll).
4. Confirm MatDialog usage (if any remaining) is unaffected — `.gcal-popover-card`
   class is absent there.

## Files changed

| File | Change |
|------|--------|
| `task-create-edit-modal.component.html` | Add `<div class="gcal-scroll-body">` wrapper around `.gcal-modal` |
| `task-create-edit-modal.component.scss` | Flex layout on card, new `.gcal-scroll-body`, `flex-shrink: 0` on close-row and actions |
