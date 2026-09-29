import { expect, Locator, Page, test } from '@playwright/test';
import { LoginPage } from '../../../Page objects/Login.page';
import { generateRandmString } from '../../../helper-functions';
import {
  BackendConfigurationPropertiesPage,
  PropertyCreateUpdate,
} from '../BackendConfigurationProperties.page';
import {
  BackendConfigurationPropertyWorkersPage,
  PropertyWorker,
} from '../BackendConfigurationPropertyWorkers.page';
import { customerDatabase, runMariadbSql } from '../db-helpers';
import { UI_TIMEOUT } from '../wait-helpers';

// #1335 phase 1 — Medarbejdere shows one column per mobile app: Compliance /
// Ad-hoc / Time / Archive. They replace the eForm / Ad hoc / Time / Archive
// permission chips and the Model & OS / Software version columns.
//
// WHAT THIS PROTECTS: line 1 of each cell has three states, and the rule the
// product owner set is that missing data is NEVER red:
//   green "Ja"               — the app has reported a version (line 2 shows it);
//   red "Nej"                — the worker has no access to the app;
//   grey "Ikke registreret"  — has access, nothing reported yet.
// A freshly created worker has reported nothing, so every app they may use must
// be grey; only the apps they may not use are red. Once the Time app reports a
// version (seeded straight on the login, as the app itself does on sign-in), the
// Time cell turns green with the version and the device in its tooltip.

const rand = generateRandmString(5);

const property: PropertyCreateUpdate = {
  name: `AppVer ${rand}`,
  chrNumber: generateRandmString(5),
  address: generateRandmString(5),
  cvrNumber: '1111111',
};

// Time registration on; no task management (Ad-hoc) and no archive access.
const worker: PropertyWorker = {
  name: `Apv${rand}`,
  surname: 'Versions',
  language: 'Dansk',
  properties: [property.name],
  workerEmail: `app-versions-${rand}@example.com`,
  timeRegistrationEnabled: true,
};
const workerFullName = `${worker.name} ${worker.surname}`;

type AppKey = 'compliance' | 'adhoc' | 'time' | 'archive';
type AppState = 'installed' | 'no-access' | 'not-registered';

const STATE_TEXT: Record<AppState, string> = {
  'installed': 'Ja',
  'no-access': 'Nej',
  'not-registered': 'Ikke registreret',
};

function appCell(row: Locator, app: AppKey): Locator {
  return row.locator(`[data-app="${app}"]`);
}

async function expectAppState(row: Locator, app: AppKey, state: AppState): Promise<void> {
  const cell = appCell(row, app);
  await expect(cell, `${app} cell state`).toHaveAttribute('data-app-state', state, { timeout: UI_TIMEOUT });
  await expect(cell.locator('.app-install-state'), `${app} cell label`).toHaveText(STATE_TEXT[state], {
    timeout: UI_TIMEOUT,
  });
}

/**
 * Writes a Time-app version onto the worker's login, as flutter-time does on every
 * sign-in (EformUser.TimeRegistration*). No API sets these from the web UI.
 */
async function seedTimeAppVersion(email: string): Promise<void> {
  if (!/^[a-z0-9-]+@example\.com$/.test(email)) {
    throw new Error(`Refusing to build SQL for email ${email}`);
  }
  const sql =
    `UPDATE AspNetUsers SET TimeRegistrationSoftwareVersion = '4.0.36', ` +
    `TimeRegistrationModel = 'Pixel 8', TimeRegistrationManufacturer = 'Google', ` +
    `TimeRegistrationOsVersion = '15' WHERE Email = '${email}'; SELECT ROW_COUNT();`;
  const stdout = await runMariadbSql(sql, `seed a Time app version for ${email}`, customerDatabase('Angular'));
  expect(stdout.trim(), `exactly one login (${email}) must have been updated`).toBe('1');
}

/** Re-fetches index-device-user by flipping the resigned filter on and off again. */
async function reloadWorkerList(workersPage: BackendConfigurationPropertyWorkersPage): Promise<void> {
  await workersPage.setShowResignedFilter(true);
  await workersPage.setShowResignedFilter(false);
}

/** The header cell whose whole text is `text` (callers pass plain words, no regex characters). */
function headerCell(page: Page, text: string): Locator {
  return page.locator('mtx-grid .mat-mdc-header-cell').filter({ hasText: new RegExp(`^\\s*${text}\\s*$`) });
}

test.describe('Property-worker app columns', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('http://localhost:4200');
    // login() already waits for #newEFormBtn, so the app is loaded when it returns.
    await new LoginPage(page).login();
  });

  test('show Compliance / Ad-hoc / Time / Archive with ja / nej / not-registered states', async ({ page }) => {
    // 5 min: login (up to 2 min on a cold app), one property create, one device-user
    // create whose SDK provisioning call is the slow part (SLOW_API_TIMEOUT), then two
    // list refreshes (API_TIMEOUT each). Every wait inside is individually bounded;
    // this is only their sum.
    test.setTimeout(300000);

    const propertiesPage = new BackendConfigurationPropertiesPage(page);
    await propertiesPage.goToProperties();
    await propertiesPage.createProperty(property);

    const workersPage = new BackendConfigurationPropertyWorkersPage(page);
    await workersPage.goToPropertyWorkers();
    await workersPage.create(worker);

    // Headers: the four app columns, and the old permission / device columns gone.
    for (const header of ['Compliance', 'Ad-hoc', 'Time', 'Archive']) {
      await expect(headerCell(page, header), `"${header}" column header`).toHaveCount(1, { timeout: UI_TIMEOUT });
    }
    await expect(headerCell(page, 'eForm'), 'the old "eForm" permission column is gone').toHaveCount(0, { timeout: UI_TIMEOUT });
    await expect(headerCell(page, 'Ad hoc'), 'the old "Ad hoc" permission column is gone').toHaveCount(0, { timeout: UI_TIMEOUT });

    const row = page.locator('.mat-mdc-row').filter({ hasText: workerFullName });
    await expect(row, 'the created worker must show up in the device-user table').toHaveCount(1, {
      timeout: UI_TIMEOUT,
    });

    // Nothing reported yet: every app the worker may use is grey, never red.
    await expectAppState(row, 'compliance', 'not-registered');
    await expectAppState(row, 'time', 'not-registered');
    // No access: red.
    await expectAppState(row, 'adhoc', 'no-access');
    await expectAppState(row, 'archive', 'no-access');
    await expect(row.locator('.app-install-version'), 'no version is shown before one is reported').toHaveCount(0, { timeout: UI_TIMEOUT });

    // The Time app reports a version -> green "Ja" with the version on line 2.
    await seedTimeAppVersion(worker.workerEmail!);
    await reloadWorkerList(workersPage);

    await expectAppState(row, 'time', 'installed');
    const timeCell = appCell(row, 'time');
    await expect(timeCell.locator('.app-install-version')).toHaveText('4.0.36', { timeout: UI_TIMEOUT });
    // The other apps are untouched by the Time report.
    await expectAppState(row, 'compliance', 'not-registered');
    await expectAppState(row, 'archive', 'no-access');

    // Model and OS live in the tooltip. mat-tooltip renders in the CDK overlay on
    // <body>, not inside the row.
    await timeCell.hover({ timeout: UI_TIMEOUT });
    const tooltip = page.locator('.cdk-overlay-container .mat-mdc-tooltip');
    await expect(tooltip, 'the Time cell tooltip names the reporting device').toContainText('Pixel 8', {
      timeout: UI_TIMEOUT,
    });
    await expect(tooltip).toContainText('Google', { timeout: UI_TIMEOUT });
  });
});
