# Calendar Inactive Completion Circle — Implementation Plan

**Goal:** Hide the completion circle button on calendar event blocks that are both inactive (`task.status === false`) and not completed (`task.completed === false`).

**Architecture:** Single `*ngIf` guard added to the existing completion button in the task-block template. No service, model, or CSS changes needed.

**Spec:** `docs/superpowers/specs/2026-06-05-calendar-inactive-event-completion-circle-design.md`

---

## Change

**File:** `eform-client/src/app/plugins/modules/backend-configuration-pn/modules/calendar/components/calendar-task-block/calendar-task-block.component.html`

Add `*ngIf="task.status || task.completed"` to the `<button class="completion-btn">` element.

**Visibility rule:** button hidden only when `status === false && completed === false`; shown in all other cases.
