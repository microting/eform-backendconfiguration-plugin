import { test, expect } from '@playwright/test';
import { LoginPage } from '../../../Page objects/Login.page';
import { generateRandmString } from '../../../helper-functions';
import { CalendarUiEnhancementsPage } from '../calendar-ui-enhancements.page';
import { TaskListPage } from '../task-list.page';
import {
  BackendConfigurationPropertiesPage,
  PropertyCreateUpdate,
} from '../BackendConfigurationProperties.page';
import {
  BackendConfigurationPropertyWorkersPage,
  PropertyWorker,
} from '../BackendConfigurationPropertyWorkers.page';

/**
 * Task list INLINE RENAME suite (#1126, shard b).
 *
 * Before this change the whole Task-name cell was a link that opened the
 * shared edit modal. Now the title TEXT opens an in-cell `<input matInput>`
 * and the modal moved to a hover-revealed icon button beside it, so the two
 * affordances have to be proved separately — that is the shape of this suite.
 *
 * DOM contract (task-list-table.component.html `titleTpl`), all by id/class,
 * never by display text:
 *   - `.tl-title-text` / `#taskListTitleText-<arpId>` — read-only title;
 *     clicking it starts the editor.
 *   - `#taskListTitleInput-<arpId>` — the editor input (`[id]`, not
 *     `[attr.id]`, so MatInput's host binding does not overwrite it).
 *   - `#taskListTitleError-<arpId>` / `.tl-title-error` — the inline error.
 *   - `.tl-title-modal-btn` / `#taskListEditModalBtn-<arpId>` — opens the full
 *     edit modal. Always rendered (only its opacity animates on row hover), so
 *     it is click-reachable without an explicit hover.
 *
 * `TaskListPage.openEditModal()` was retargeted from `a.ctl-link` to
 * `.tl-title-modal-btn` for the same reason; the sibling suites that call it
 * (shard h) go through that helper and so follow automatically.
 *
 * WHY THE ROW-SELECTION ASSERTIONS ARE NOT INCIDENTAL. mtx-grid binds
 * `(click)="_selectRow(...)"` on the `<tr>`, and with `[rowSelectable]` that
 * handler CLEARS the entire selection and toggles the clicked row. Every
 * interactive element of the title cell therefore has to stop propagation, or
 * clicking into the editor would silently wipe a batch selection the admin had
 * just built. IR5 pins exactly that.
 *
 * Seed: one property + one worker + one calendar-created task. It must be
 * created from the CALENDAR (not the task list), because only the wizard's
 * create path sets `AreaRule.CreatedInGuide`, and `BuildUpdateModel` — which
 * every task-list write action goes through — refuses anything else with
 * "Task not found".
 *
 * `describe.serial`: the tests rename the same row in sequence, and each one
 * reads the name the previous one left behind.
 *
 * IR1: clicking the title text opens the editor, seeded with the current name,
 *      autofocused and fully text-selected.
 * IR2: Esc restores the original name and closes the editor without a request.
 * IR3: an empty/whitespace name is refused — edit mode is RETAINED, the inline
 *      error shows, and no rename request is issued.
 * IR4: Enter saves; the POST succeeds and the grid shows the new name.
 * IR5: the editor never disturbs row selection (opening, typing, or Esc).
 * IR6: the full edit modal is still reachable from the icon, seeded with the
 *      renamed title — and opening it does not disturb the selection either.
 */

const property: PropertyCreateUpdate = {
  name: `ir-${generateRandmString(5)}`,
  chrNumber: generateRandmString(5),
  address: generateRandmString(5),
  cvrNumber: '1111111',
};

const worker: PropertyWorker = {
  name: generateRandmString(5),
  surname: generateRandmString(5),
  language: 'Dansk',
  properties: [property.name],
  workerEmail: generateRandmString(5) + '@test.com',
};

const rand = generateRandmString(6);
const originalName = `ir-task-${rand}`;
const renamedName = `ir-renamed-${rand}`;

/**
 * Narrows the grid to the one seeded row and returns its AreaRulePlanning id,
 * which every per-row id in the title cell is suffixed with.
 *
 * Read from the row's OWN title element rather than from the Id column: the Id
 * cell renders `123 (456)` (planning id in a `<small>`), so parsing it would
 * be a second, avoidable format dependency.
 */
async function focusRow(taskListPage: TaskListPage, page: any, name: string): Promise<string> {
  await taskListPage.goto();
  await taskListPage.search(name);
  await expect(taskListPage.row(name)).toBeVisible({ timeout: 20000 });
  const id = await taskListPage.titleText(name).getAttribute('id');
  if (!id) {
    throw new Error(`Row "${name}" has no .tl-title-text id`);
  }
  return id.replace('taskListTitleText-', '');
}

test.describe.serial('Task list — inline rename of the task name', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('http://localhost:4200');
    await new LoginPage(page).login();
    await page.waitForTimeout(1500);
  });

  // =======================================================================
  // CLEANUP. Same house contract as every sibling suite: a 60s wall-clock
  // budget, never fail the run, never eat the shard's time. Three things are
  // tightened, each of which only adds work on a path that previously did
  // nothing at all:
  //
  //   1. The race resolves with a SENTINEL. `Promise.race([cleanup(), new
  //      Promise(r => setTimeout(r, 60000))])` swallows a hang in complete
  //      silence — the catch is reachable only from a throw — so a cleanup
  //      that stalls looked exactly like one that succeeded. Now a stall is
  //      logged with the names of the rows it may have left behind.
  //   2. The worker phase and the property phase are guarded SEPARATELY
  //      inside the one shared budget. Before, a throw while clearing
  //      workers skipped the property deletion entirely and handed the
  //      leftover property to the next spec in the shard.
  //   3. A verification read afterwards, so "cleanup ran" and "the rows are
  //      gone" are no longer the same claim.
  //
  // The budget is a single deadline shared by all phases, so the worst case
  // is still ~60s — deliberately not raised.
  //
  // Idempotence: `clearTable()` is a no-op on an empty grid (properties
  // returns early at 0 rows, workers loops zero times), so a row that some
  // earlier step already removed is tolerated rather than retried.
  //
  // A phase that TIMES OUT aborts the rest: its operations are still driving
  // this page, and starting the next phase on top of them would be worse
  // than skipping it. A phase that THROWS is idle, so the next one runs —
  // except for the login, without which nothing downstream can succeed and
  // every later phase would just burn the budget on doomed waits.
  // =======================================================================
  test.afterAll(async ({ browser }) => {
    const CLEANUP_BUDGET_MS = 60000;
    const deadline = Date.now() + CLEANUP_BUDGET_MS;
    const problems: string[] = [];
    let aborted = false;

    // Never throws. Returns whether the phase actually completed.
    const phase = async (label: string, fn: () => Promise<void>): Promise<boolean> => {
      if (aborted) {
        problems.push(`${label}: skipped, an earlier phase did not complete`);
        return false;
      }
      const budget = deadline - Date.now();
      if (budget <= 0) {
        problems.push(`${label}: skipped, cleanup budget exhausted`);
        aborted = true;
        return false;
      }
      let timer: ReturnType<typeof setTimeout> | undefined;
      try {
        const outcome = await Promise.race([
          fn().then(() => 'done' as const),
          new Promise<'timeout'>(resolve => {
            timer = setTimeout(() => resolve('timeout'), budget);
          }),
        ]);
        if (outcome === 'timeout') {
          aborted = true;
          problems.push(`${label}: timed out after ${budget}ms`);
          return false;
        }
        return true;
      } catch (err: any) {
        problems.push(`${label}: ${err?.message ?? err}`);
        return false;
      } finally {
        if (timer) {
          clearTimeout(timer);
        }
      }
    };

    const page = await browser.newPage();
    try {
      const workersPage = new BackendConfigurationPropertyWorkersPage(page);
      const propertiesPage = new BackendConfigurationPropertiesPage(page);

      const loggedIn = await phase('login', async () => {
        await page.goto('http://localhost:4200');
        await new LoginPage(page).login();
      });
      if (!loggedIn) {
        // Nothing below can work unauthenticated; stop rather than spend the
        // remaining budget on waits that are certain to time out.
        aborted = true;
      }

      // Workers first: the property cannot go while a worker is assigned to
      // it. Waiting for the grid host instead of only sleeping matters here —
      // counting rows before the page has rendered returns 0 and makes
      // clearTable() a silent no-op, which is precisely the failure mode this
      // block exists to surface.
      await phase('clear workers', async () => {
        await workersPage.goToPropertyWorkers();
        await workersPage.newDeviceUserBtn().waitFor({ state: 'visible', timeout: 15000 });
        await page.waitForTimeout(1000);
        await workersPage.clearTable();
      });

      await phase('clear properties', async () => {
        await propertiesPage.goToProperties();
        await page.locator('app-properties-table').waitFor({ state: 'visible', timeout: 15000 });
        await page.waitForTimeout(1000);
        await propertiesPage.clearTable();
      });

      await phase('verify', async () => {
        await propertiesPage.goToProperties();
        await page.locator('app-properties-table').waitFor({ state: 'visible', timeout: 15000 });
        const propertiesLeft = await page.locator('app-properties-table .mat-mdc-row').count();
        if (propertiesLeft > 0) {
          problems.push(`verify: ${propertiesLeft} property row(s) still present`);
        }
        await workersPage.goToPropertyWorkers();
        await workersPage.newDeviceUserBtn().waitFor({ state: 'visible', timeout: 15000 });
        const workersLeft = await workersPage.rowNum();
        if (workersLeft > 0) {
          problems.push(`verify: ${workersLeft} worker row(s) still present`);
        }
      });
    } catch (err: any) {
      problems.push(`cleanup harness: ${err?.message ?? err}`);
    } finally {
      if (problems.length > 0) {
        console.log(
          '[task-list-inline-rename] afterAll cleanup INCOMPLETE (non-fatal) — ' +
          `may have left property "${property.name}" / worker ` +
          `"${worker.name} ${worker.surname}" for the next spec in this shard: ` +
          problems.join(' | '),
        );
      }
      try { await page.close(); } catch {}
    }
  });

  test('seed: create property + worker + task', async ({ page }) => {
    test.setTimeout(600000);

    const propertiesPage = new BackendConfigurationPropertiesPage(page);
    const workersPage = new BackendConfigurationPropertyWorkersPage(page);

    await propertiesPage.goToProperties();
    await propertiesPage.createProperty(property);
    await workersPage.goToPropertyWorkers();
    await workersPage.create(worker);

    const calendarPage = new CalendarUiEnhancementsPage(page);
    await calendarPage.goToCalendar();
    await calendarPage.ensureSidebarOpen();
    await calendarPage.selectProperty(property.name);
    await page.waitForTimeout(1000);

    await calendarPage.openCreateModalAtSlot(0, 9);
    await calendarPage.fillAndSaveEvent(originalName);
  });

  // =======================================================================
  // IR1 — click-to-edit. The seeded value, the autofocus and the full text
  // selection are all part of the interaction spec: the editor is meant to be
  // type-over-ready, so a user who clicks and types replaces the name rather
  // than appending to it.
  // =======================================================================
  test('IR1: clicking the title opens an autofocused, text-selected editor', async ({ page }) => {
    test.setTimeout(180000);
    const taskListPage = new TaskListPage(page);
    const arpId = await focusRow(taskListPage, page, originalName);

    await taskListPage.startInlineRename(originalName);

    const state = await page.evaluate((id) => {
      const input = document.getElementById(`taskListTitleInput-${id}`) as HTMLInputElement | null;
      return input === null ? null : {
        value: input.value,
        focused: document.activeElement === input,
        selectionStart: input.selectionStart,
        selectionEnd: input.selectionEnd,
      };
    }, arpId);

    expect(state).not.toBeNull();
    expect(state!.value).toBe(originalName);
    expect(state!.focused).toBe(true);
    expect(state!.selectionStart).toBe(0);
    expect(state!.selectionEnd).toBe(originalName.length);

    // The read-only title is replaced, not merely covered.
    await expect(page.locator(`#taskListTitleText-${arpId}`)).toHaveCount(0);
  });

  // =======================================================================
  // IR2 — Esc cancels and restores. Also pins that Escape does not escape the
  // cell: it must not reach any CDK overlay ancestor (no dialog opens/closes)
  // and must not fire a rename.
  // =======================================================================
  test('IR2: Esc restores the original name without saving', async ({ page }) => {
    test.setTimeout(180000);
    const taskListPage = new TaskListPage(page);
    const arpId = await focusRow(taskListPage, page, originalName);

    let renameCalls = 0;
    page.on('request', r => {
      if (r.url().includes('/api/backend-configuration-pn/task-list/rename')) renameCalls++;
    });

    await taskListPage.startInlineRename(originalName);
    await taskListPage.setInlineRenameValue('esc discards this');
    await taskListPage.cancelInlineRenameWithEscape();

    await expect(page.locator(`#taskListTitleInput-${arpId}`)).toHaveCount(0);
    await expect(page.locator(`#taskListTitleText-${arpId}`)).toHaveText(originalName);
    await expect(page.locator('mat-dialog-container')).toHaveCount(0);
    expect(renameCalls, 'Esc must not issue a rename request').toBe(0);
  });

  // =======================================================================
  // IR3 — empty is refused and edit mode is RETAINED. The modal declares the
  // title `Validators.required`; the inline editor honours the same rule
  // rather than treating a cleared field as "delete the name".
  //
  // Asserted on whitespace-only, which also covers plain empty: both trim to
  // "" and take the identical branch, and whitespace is the case a naive
  // `if (!value)` check would let through.
  // =======================================================================
  test('IR3: a whitespace-only name is refused and edit mode is kept', async ({ page }) => {
    test.setTimeout(180000);
    const taskListPage = new TaskListPage(page);
    const arpId = await focusRow(taskListPage, page, originalName);

    let renameCalls = 0;
    page.on('request', r => {
      if (r.url().includes('/api/backend-configuration-pn/task-list/rename')) renameCalls++;
    });

    await taskListPage.startInlineRename(originalName);
    await taskListPage.setInlineRenameValue('   ');
    await taskListPage.titleInput().press('Enter');
    await page.waitForTimeout(800);

    // Still editing, with the offending text still there to be corrected.
    await expect(page.locator(`#taskListTitleInput-${arpId}`)).toBeVisible();
    await expect(page.locator(`#taskListTitleError-${arpId}`)).toBeVisible();
    expect(renameCalls, 'an empty name must not reach the server').toBe(0);

    // Leave the row as we found it for the next test in the serial chain.
    await taskListPage.cancelInlineRenameWithEscape();
    await expect(page.locator(`#taskListTitleText-${arpId}`)).toHaveText(originalName);
  });

  // =======================================================================
  // IR4 — the happy path. The POST body is asserted, not just the outcome:
  // the endpoint takes a one-element `taskIds` list (single-row action on the
  // batch rail), and a regression that sent the whole selection would still
  // make the grid look right for a one-row selection.
  // =======================================================================
  test('IR4: Enter saves and the grid shows the new name', async ({ page }) => {
    test.setTimeout(180000);
    const taskListPage = new TaskListPage(page);
    const arpId = await focusRow(taskListPage, page, originalName);

    const renameResponse = page.waitForResponse(
      r => r.url().includes('/api/backend-configuration-pn/task-list/rename')
        && r.request().method() === 'POST',
      { timeout: 60000 },
    );

    await taskListPage.startInlineRename(originalName);
    await taskListPage.setInlineRenameValue(renamedName);
    await taskListPage.commitInlineRenameWithEnter();

    const response = await renameResponse;
    const body = await response.json();
    expect(body.success, `rename returned success=false: ${body.message}`).toBe(true);
    expect(JSON.parse(response.request().postData() ?? '{}')).toEqual({
      taskIds: [Number(arpId)],
      title: renamedName,
    });

    // The editor closed and the refreshed grid carries the new name. The name
    // filter still holds the OLD name, so re-search before asserting.
    await expect(page.locator(`#taskListTitleInput-${arpId}`)).toHaveCount(0);
    await taskListPage.search(renamedName);
    await expect(taskListPage.row(renamedName)).toBeVisible({ timeout: 20000 });
    await expect(page.locator(`#taskListTitleText-${arpId}`)).toHaveText(renamedName);
  });

  // =======================================================================
  // IR5 — the editor must not disturb row selection. See the class comment:
  // mtx-grid's `<tr>` click handler clears the whole selection, so without
  // stopPropagation in the title cell a click into the editor would wipe a
  // batch selection. Probed at three points (open, type, cancel).
  // =======================================================================
  test('IR5: editing the title never disturbs the row selection', async ({ page }) => {
    test.setTimeout(180000);
    const taskListPage = new TaskListPage(page);
    const arpId = await focusRow(taskListPage, page, renamedName);

    await taskListPage.selectRow(renamedName);
    const selectionCount = page.locator('#taskListSelectionCount');
    await expect(selectionCount).toBeVisible();
    const checkbox = taskListPage.rowCheckbox(renamedName).locator('input[type="checkbox"]');
    await expect(checkbox).toBeChecked();

    await taskListPage.startInlineRename(renamedName);
    await expect(selectionCount).toBeVisible();

    await taskListPage.setInlineRenameValue(`${renamedName}-typing`);
    await page.waitForTimeout(300);
    await expect(selectionCount).toBeVisible();

    await taskListPage.cancelInlineRenameWithEscape();
    await expect(selectionCount).toBeVisible();
    await expect(taskListPage.rowCheckbox(renamedName).locator('input[type="checkbox"]')).toBeChecked();
    await expect(page.locator(`#taskListTitleText-${arpId}`)).toHaveText(renamedName);
  });

  // =======================================================================
  // IR6 — the modal is still reachable. The title click no longer opens it,
  // so this is the only remaining route to the full editor from this page;
  // if the icon regressed, the page would lose every field except the name.
  // =======================================================================
  test('IR6: the full edit modal is still reachable from the icon', async ({ page }) => {
    test.setTimeout(180000);
    const taskListPage = new TaskListPage(page);
    const arpId = await focusRow(taskListPage, page, renamedName);

    await taskListPage.selectRow(renamedName);
    await expect(page.locator('#taskListSelectionCount')).toBeVisible();

    await expect(page.locator(`#taskListEditModalBtn-${arpId}`)).toHaveCount(1);
    await taskListPage.openEditModal(renamedName);

    await expect(page.locator('mat-dialog-container')).toBeVisible();
    // Seeded from the row, so this also re-proves the rename persisted.
    await expect(page.locator('#calendarEventTitle')).toHaveValue(renamedName);

    await page.locator('#calendarEventCancelBtn').click();
    await expect(page.locator('mat-dialog-container')).toBeHidden({ timeout: 15000 });

    // Opening the modal must not have wiped the selection either.
    await expect(page.locator('#taskListSelectionCount')).toBeVisible();
  });
});
