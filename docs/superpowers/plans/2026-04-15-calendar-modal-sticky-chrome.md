# Calendar Modal Sticky Chrome — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Keep the close X (top-right) and Save button (bottom-right) of the calendar task create/edit modal visible while the form body scrolls.

**Architecture:** Convert `.gcal-popover-card` from a single scrolling block into a 3-zone flex column (close-row · scrolling body · actions). Only the middle zone scrolls.

**Tech Stack:** Angular 17 + SCSS. No TS changes, no new i18n keys, no new tests.

**Dev mode assumption:** Full dev mode — edits land in the host app under
`eform-angular-frontend/eform-client/src/app/plugins/modules/backend-configuration-pn/...`,
then synced back via `devgetchanges.sh` before committing in the plugin repo.
If the user is not in dev mode, edit the plugin repo paths directly (listed under "source paths" in each task).

---

### Task 1: Wrap the modal body in a new scroll container

**Files:**
- Modify (dev mode, host app): `eform-angular-frontend/eform-client/src/app/plugins/modules/backend-configuration-pn/modules/calendar/modals/task-create-edit-modal/task-create-edit-modal.component.html`
- Source path (for devgetchanges or non-dev mode): `eform-backendconfiguration-plugin/eform-client/src/app/plugins/modules/backend-configuration-pn/modules/calendar/modals/task-create-edit-modal/task-create-edit-modal.component.html`

- [ ] **Step 1: Add the `<div class="gcal-scroll-body">` opening tag**

Find line 8:

```html
  <div class="gcal-modal">
```

Replace with:

```html
  <div class="gcal-scroll-body">
  <div class="gcal-modal">
```

- [ ] **Step 2: Add the closing `</div>` for the new wrapper**

Find the closing `</div>` of the `.gcal-modal` block (the one before `<div class="gcal-actions" *ngIf="!isReadonly">` — currently line 243). It looks like:

```html
    </div>
  </div>

  <div class="gcal-actions" *ngIf="!isReadonly">
```

Replace with:

```html
    </div>
  </div>
  </div>

  <div class="gcal-actions" *ngIf="!isReadonly">
```

(The extra `</div>` closes the new `.gcal-scroll-body` wrapper. `.gcal-actions` remains a direct child of `.gcal-popover-card`.)

---

### Task 2: Update the SCSS to make the card a flex column and the new wrapper the scroller

**Files:**
- Modify (dev mode, host app): `eform-angular-frontend/eform-client/src/app/plugins/modules/backend-configuration-pn/modules/calendar/modals/task-create-edit-modal/task-create-edit-modal.component.scss`
- Source path: `eform-backendconfiguration-plugin/eform-client/src/app/plugins/modules/backend-configuration-pn/modules/calendar/modals/task-create-edit-modal/task-create-edit-modal.component.scss`

- [ ] **Step 1: Change `.gcal-popover-card` from scrolling block to flex column**

Find (lines 1–8):

```scss
// Popover card wrapper — used when rendered via CDK Overlay
.gcal-popover-card {
  background: white;
  border-radius: 8px;
  box-shadow: 0 8px 28px rgba(0, 0, 0, 0.28);
  max-height: 90vh;
  overflow-y: auto;
}
```

Replace with:

```scss
// Popover card wrapper — used when rendered via CDK Overlay
.gcal-popover-card {
  background: white;
  border-radius: 8px;
  box-shadow: 0 8px 28px rgba(0, 0, 0, 0.28);
  max-height: 90vh;
  display: flex;
  flex-direction: column;
}

// Scrolling middle zone — keeps close-row and actions pinned
.gcal-scroll-body {
  flex: 1 1 auto;
  overflow-y: auto;
  min-height: 0; // required so a flex child can shrink and scroll
}
```

- [ ] **Step 2: Add `flex-shrink: 0` to `.gcal-actions`**

Find (currently lines 10–14):

```scss
.gcal-actions {
  display: flex;
  justify-content: flex-end;
  padding: 8px 24px 16px 24px;
}
```

Replace with:

```scss
.gcal-actions {
  display: flex;
  justify-content: flex-end;
  padding: 8px 24px 16px 24px;
  flex-shrink: 0;
}
```

- [ ] **Step 3: Add `flex-shrink: 0` to `.gcal-close-row`**

Find (currently lines 16–20):

```scss
.gcal-close-row {
  display: flex;
  justify-content: flex-end;
  padding: 8px 8px 0 0;
}
```

Replace with:

```scss
.gcal-close-row {
  display: flex;
  justify-content: flex-end;
  padding: 8px 8px 0 0;
  flex-shrink: 0;
}
```

---

### Task 3: Manual verification

- [ ] **Step 1: Build / rebuild the Angular client**

The dev server should hot-reload. If not running:

```bash
cd /home/rene/Documents/workspace/microting/eform-angular-frontend/eform-client
npm start
```

- [ ] **Step 2: Open the calendar, create or edit a task**

Pick a property with many assignees / tags so the modal content overflows the viewport.

- [ ] **Step 3: Scroll the modal body**

Expected:
- Close X stays pinned top-right of the card.
- Save button stays pinned bottom-right (visible when not in readonly mode).
- Form fields scroll between them.
- No horizontal scrollbar introduced.

- [ ] **Step 4: Resize window to tall viewport so content no longer overflows**

Expected: body just stops scrolling; no layout change.

---

### Task 4: Sync, commit, push

- [ ] **Step 1: Run `devgetchanges.sh` from the plugin repo**

```bash
cd /home/rene/Documents/workspace/microting/eform-backendconfiguration-plugin
./devgetchanges.sh
```

- [ ] **Step 2: Discard any artifacts brought back by the script**

```bash
cd /home/rene/Documents/workspace/microting/eform-backendconfiguration-plugin
git checkout '*.csproj' '*.conf.ts' '*.xlsx' '*.docx' 2>/dev/null || true
git status
```

Review `git status`. Expected intended files:
- `eform-client/src/app/plugins/modules/backend-configuration-pn/modules/calendar/modals/task-create-edit-modal/task-create-edit-modal.component.html`
- `eform-client/src/app/plugins/modules/backend-configuration-pn/modules/calendar/modals/task-create-edit-modal/task-create-edit-modal.component.scss`

If any other files show up unexpectedly, `git checkout` them before committing.

- [ ] **Step 3: Commit on `feat/calendar-i18n-audit`**

```bash
git add \
  eform-client/src/app/plugins/modules/backend-configuration-pn/modules/calendar/modals/task-create-edit-modal/task-create-edit-modal.component.html \
  eform-client/src/app/plugins/modules/backend-configuration-pn/modules/calendar/modals/task-create-edit-modal/task-create-edit-modal.component.scss
git commit -m "$(cat <<'EOF'
style(calendar): keep close X and Save button visible when modal body scrolls

Wrap modal body in .gcal-scroll-body so .gcal-popover-card becomes a flex
column with fixed close-row and actions, and only the middle zone scrolls.

Co-Authored-By: Claude Opus 4.6 (1M context) <noreply@anthropic.com>
EOF
)"
```

- [ ] **Step 4: Push to PR branch**

```bash
git push
```

Wait for CI on PR #754.
