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
 * Task list COMPLIANCE-vs-STATUS rendering suite (shard h).
 *
 * Regression cover for the grid showing a live green Compliance "Ja" on
 * tasks that are INACTIVE, contradicting the calendar edit modal — which
 * treats overdue visibility as not-applicable while a task is inactive and
 * renders BOTH compliance toggles off
 * (`task-create-edit-modal.component.html`, `[checked]="statusControl.value
 * && complianceEnabledControl.value"` / `... && !complianceEnabledControl
 * .value"`).
 *
 * The stored `ComplianceEnabled` flag really is dormant while a task is
 * inactive: deactivation soft-deletes every `Compliance` row for the
 * planning, and the nightly overdue-move job walks `Compliance` rows only.
 * So the grid must render the empty-value placeholder `--` (the same
 * convention mtx-grid uses for the untemplated columns), not a badge.
 *
 * CI1: a freshly created task is Active + Compliance "Ja" — `.badge.ja` in
 *      BOTH the Aktiv and the Compliance cell (the modal defaults are
 *      `statusControl = true`, `complianceEnabledControl = true`).
 * CI2: deactivating via the shared edit modal leaves the compliance
 *      toggles both OFF while the stored flag stays true — the modal
 *      semantics the grid has to mirror.
 * CI3: the grid's Compliance cell then renders `--` with NO badge, while
 *      the Aktiv cell renders `.badge.nej`. The Aktiv column is deliberately
 *      NOT gated.
 * CI4: the CSV export's Compliance column agrees with the on-screen cell in
 *      both states (it had the identical ungated ternary).
 *
 * Selector discipline: rows/cells are matched by mtx-grid column class
 * (`.mat-column-status` / `.mat-column-compliance`) and badge class
 * (`.badge.ja` / `.badge.nej`), never by the Danish display text.
 *
 * Seed: one property + one worker + one calendar-created task. `describe
 * .serial` — CI2 mutates the task the later tests read.
 */

const property: PropertyCreateUpdate = {
  name: `tci-${generateRandmString(5)}`,
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

const task = `tci-task-${generateRandmString(6)}`;

/**
 * The CSV row for `task`, as its trailing `[Active, Compliance]` pair.
 * Those two columns are last on the line and are always plain tokens, so
 * splitting on `;` and taking the final two entries is unaffected by any
 * quoted, semicolon-bearing field earlier in the row.
 */
function csvActiveAndCompliance(lines: string[], taskName: string): [string, string] {
  const line = lines.find(l => l.includes(taskName));
  if (!line) {
    throw new Error(`No CSV row found for task "${taskName}"`);
  }
  const cells = line.trim().split(';');
  return [cells[cells.length - 2], cells[cells.length - 1]];
}

test.describe.serial('Task list compliance rendering for inactive tasks', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('http://localhost:4200');
    await new LoginPage(page).login();
    await page.waitForTimeout(1500);
  });

  test.afterAll(async ({ browser }) => {
    const page = await browser.newPage();
    const cleanup = async () => {
      await page.goto('http://localhost:4200');
      await new LoginPage(page).login();

      const workersPage = new BackendConfigurationPropertyWorkersPage(page);
      await workersPage.goToPropertyWorkers();
      await page.waitForTimeout(1000);
      await workersPage.clearTable();

      const propertiesPage = new BackendConfigurationPropertiesPage(page);
      await propertiesPage.goToProperties();
      await page.waitForTimeout(1000);
      await propertiesPage.clearTable();
    };
    try {
      await Promise.race([
        cleanup(),
        new Promise(resolve => setTimeout(resolve, 60000)),
      ]);
    } catch (err: any) {
      console.log(`afterAll cleanup failed (non-fatal): ${err?.message ?? err}`);
    }
    try { await page.close(); } catch {}
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
    await calendarPage.fillAndSaveEvent(task);
  });

  // =======================================================================
  // CI1 — baseline: an ACTIVE task renders the Ja badge in both columns,
  // and the CSV agrees. This is the "unchanged behaviour" half of the fix.
  // =======================================================================
  test('CI1: an active task renders Ja badges and the CSV matches', async ({ page }) => {
    test.setTimeout(180000);
    const taskListPage = new TaskListPage(page);

    await taskListPage.goto();
    await taskListPage.selectProperty(property.name);
    await expect(taskListPage.row(task)).toBeVisible();

    await expect(taskListPage.columnCell(task, 'status').locator('.badge.ja')).toHaveCount(1);
    await expect(taskListPage.columnCell(task, 'compliance').locator('.badge.ja')).toHaveCount(1);

    // Compare each CSV column against ITS OWN cell — not both against the
    // compliance cell, which would pass by coincidence for a freshly created
    // task (Aktiv and Compliance are both "Ja") and would hide a real
    // divergence between the two columns.
    const statusOnScreen = (await taskListPage.columnCell(task, 'status').innerText()).trim();
    const complianceOnScreen = (await taskListPage.columnCell(task, 'compliance').innerText()).trim();
    const [csvActive, csvCompliance] = csvActiveAndCompliance(
      await taskListPage.exportCsvAndReadLines(), task);
    expect(csvActive).toBe(statusOnScreen);
    expect(csvCompliance).toBe(complianceOnScreen);
  });

  // =======================================================================
  // CI2 — the modal contract the grid must mirror: with Status inactive,
  // BOTH compliance toggles read off even though the stored flag is true.
  // Ends by saving, leaving the task inactive for CI3/CI4.
  // =======================================================================
  test('CI2: the edit modal shows both compliance toggles off when inactive', async ({ page }) => {
    test.setTimeout(180000);
    const taskListPage = new TaskListPage(page);

    await taskListPage.goto();
    await taskListPage.selectProperty(property.name);
    await taskListPage.openEditModal(task);

    // Angular Material 20 renders mat-slide-toggle as
    // `<button class="mdc-switch" role="switch" aria-checked>` — there is NO
    // `<input>` inside it, so `toBeChecked()` on `#id input` would resolve to
    // zero elements and time out. Assert `aria-checked` on the switch button.
    const toggle = (id: string) => page.locator(`#${id} button[role="switch"]`);

    // Stored state as created: active, compliance enabled.
    await expect(toggle('calendarEventStatusActive')).toHaveAttribute('aria-checked', 'true');
    await expect(toggle('calendarEventComplianceOn')).toHaveAttribute('aria-checked', 'true');

    await page.locator('#calendarEventStatusInactive').click();
    await page.waitForTimeout(300);

    // The flag is untouched by this click (`onPickStatusInactive` only sets
    // `statusControl`), yet both toggles now read off — overdue is N/A.
    await expect(toggle('calendarEventStatusInactive')).toHaveAttribute('aria-checked', 'true');
    await expect(toggle('calendarEventComplianceOn')).toHaveAttribute('aria-checked', 'false');
    await expect(toggle('calendarEventComplianceOff')).toHaveAttribute('aria-checked', 'false');

    await taskListPage.saveEditModal();
  });

  // =======================================================================
  // CI3 — the fix: no Compliance badge for an inactive task, `--` instead;
  // the Aktiv column keeps its badge.
  // =======================================================================
  test('CI3: an inactive task renders -- and no compliance badge', async ({ page }) => {
    test.setTimeout(180000);
    const taskListPage = new TaskListPage(page);

    await taskListPage.goto();
    await taskListPage.selectProperty(property.name);
    await expect(taskListPage.row(task)).toBeVisible();

    // Aktiv column is deliberately left ungated.
    await expect(taskListPage.columnCell(task, 'status').locator('.badge.nej')).toHaveCount(1);

    const complianceCell = taskListPage.columnCell(task, 'compliance');
    await expect(complianceCell.locator('.badge')).toHaveCount(0);
    await expect(complianceCell.locator('.badge.ja')).toHaveCount(0);
    expect((await complianceCell.innerText()).trim()).toBe('--');
  });

  // =======================================================================
  // CI4 — the CSV export must not keep claiming Ja once the screen says --.
  // =======================================================================
  test('CI4: the CSV compliance column matches the -- cell', async ({ page }) => {
    test.setTimeout(180000);
    const taskListPage = new TaskListPage(page);

    await taskListPage.goto();
    await taskListPage.selectProperty(property.name);
    await expect(taskListPage.row(task)).toBeVisible();

    const onScreen = (await taskListPage.columnCell(task, 'compliance').innerText()).trim();
    const [, csvCompliance] = csvActiveAndCompliance(
      await taskListPage.exportCsvAndReadLines(), task);

    expect(onScreen).toBe('--');
    expect(csvCompliance).toBe(onScreen);
  });
});
