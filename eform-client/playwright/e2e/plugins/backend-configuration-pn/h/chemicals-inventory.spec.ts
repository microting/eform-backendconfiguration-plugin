import { test, expect, Page } from '@playwright/test';
import { generateRandmString } from '../../../helper-functions';
import {
  BackendConfigurationChemicalsPage,
  CHEMICAL_SCREENS_IN_MENU_ORDER,
  ChemicalScreen,
  kemiProperty,
} from '../BackendConfigurationChemicals.page';
import { isoDateFromToday, propertyIdByName, seedRegisterChemical, seedRegisterDocument, setAdminLocale, STUB_PDF } from '../chemical-register-seed';
import { API_TIMEOUT, UI_TIMEOUT, waitForApiResponse } from '../wait-helpers';
import { clickMenuItem } from '../row-action-menu';
import { dialogReady, fillInDialog } from '../dialog-helpers';

/**
 * W1 + W2 (flutter-chemistry spec §11): register placements from the BMD register, "Kræver handling"
 * (chip, banner, red row), the digest deeplink, the side panel's remove with the hazardous-waste warning,
 * move, the three exports, and the archive block in W3. Shard h starts from a fresh database: the spec
 * seeds the register by SQL (no API writes it) and everything else through the UI. Assertions avoid UI
 * texts, so they hold in any UI language.
 */
const rand = generateRandmString(6);
const property = kemiProperty('Kemi-inv', rand);
const approved = `KemiOK${rand}`;
const expired = `KemiUD${rand}`;
const lade = `Lade-${rand}`;
const kemirum = `Kemirum-${rand}`;

/** The md5 file names of the seeded documents of `approved`; `expired` has none. */
const LABEL_MD5 = 'a1'.repeat(16);
const SDS_DA_MD5 = 'b2'.repeat(16);
const SDS_EN_MD5 = 'c3'.repeat(16);

/**
 * The register serves the md5 as the file name and the plugin proxies the PDF from chemicalbase, which CI never
 * calls: the browser gets a stub PDF for label/ and sds/, so the specs assert which file the screen asks for.
 */
const stubDocumentDownloads = (page: Page) =>
  page.route(/\/api\/backend-configuration-pn\/chemicals\/(label|sds)\//, (route) =>
    route.fulfill({ status: 200, contentType: 'application/pdf', body: STUB_PDF }));

const documentResponse = (page: Page, kind: 'label' | 'sds', md5: string) =>
  waitForApiResponse(page, `GET chemicals/${kind}/${md5}`,
    (r) => r.request().method() === 'GET' && new URL(r.url()).pathname.endsWith(`/chemicals/${kind}/${md5}`), API_TIMEOUT);

const open = (page: Page, screen: ChemicalScreen) => BackendConfigurationChemicalsPage.openLoggedIn(page, screen, property.name);

test.describe.serial('Kemi W1/W2 - inventory', () => {
  test('seed: two register products, a property and two locations', async ({ page }) => {
    test.setTimeout(180000);
    const approvedId = await seedRegisterChemical({ name: approved, registrationNo: `1-${rand}`, status: 5 });
    await seedRegisterDocument({ chemicalId: approvedId, type: 2, md5: LABEL_MD5, packaging: '5 L', revisionDate: '2026-03-01' });
    await seedRegisterDocument({ chemicalId: approvedId, type: 1, md5: SDS_DA_MD5, language: 'da' });
    await seedRegisterDocument({ chemicalId: approvedId, type: 1, md5: SDS_EN_MD5, language: 'en' });
    await seedRegisterChemical({ name: expired, registrationNo: `2-${rand}`, status: 7, useAndPossessionDeadline: isoDateFromToday(-30) });
    const kemi = await BackendConfigurationChemicalsPage.login(page);
    await kemi.createPropertyWithLocations(property, [lade, kemirum]);
  });

  test('the Kemi menu lists the five screens in order and opens Beholdning', async ({ page }) => {
    test.setTimeout(120000);
    const kemi = await BackendConfigurationChemicalsPage.login(page);

    await kemi.expandChemicalsMenu();
    const items = kemi.screenMenuButtons();
    await expect(items).toHaveCount(CHEMICAL_SCREENS_IN_MENU_ORDER.length, { timeout: UI_TIMEOUT });
    for (const [i, screen] of CHEMICAL_SCREENS_IN_MENU_ORDER.entries()) {
      await expect(items.nth(i)).toHaveAttribute('id', `backend-configuration-pn-chemicals-${screen}`, { timeout: UI_TIMEOUT });
    }
    await kemi.screenMenuButton('inventory').click({ timeout: UI_TIMEOUT });
    await page.locator('#chemicalInventoryPage').waitFor({ state: 'visible', timeout: API_TIMEOUT });
    await expect(page).toHaveURL(/\/plugins\/backend-configuration-pn\/chemicals\/inventory/, { timeout: UI_TIMEOUT });
  });

  test('register both products; the expired one requires action (red)', async ({ page }) => {
    test.setTimeout(150000);
    const kemi = await open(page, 'inventory');

    await kemi.registerPlacement(approved, lade, 'Top shelf');
    await kemi.registerPlacement(expired, lade, 'Under the sink');

    await expect(kemi.inventoryRow(expired)).toHaveClass(/chemical-row--overdue/, { timeout: UI_TIMEOUT });
    await expect(kemi.inventoryRow(approved)).not.toHaveClass(/chemical-row--/, { timeout: UI_TIMEOUT });
    await expect(page.locator('#chemicalsActionBanner')).toBeVisible({ timeout: UI_TIMEOUT });
    await page.locator('#chemicalsRequiresActionChip').click({ timeout: UI_TIMEOUT });
    await expect(page.locator('#chemicalsRequiresActionChip')).toHaveAttribute('aria-pressed', 'true', { timeout: UI_TIMEOUT });
    await expect(kemi.inventoryRow(approved)).toHaveCount(0, { timeout: UI_TIMEOUT });
    await expect(kemi.inventoryRow(expired)).toHaveCount(1, { timeout: UI_TIMEOUT });
  });

  test('the digest deeplink on the bare Kemi root opens W1 filtered (Plan D C4)', async ({ page }) => {
    test.setTimeout(90000);
    const propertyId = await propertyIdByName(property.name);
    const kemi = await BackendConfigurationChemicalsPage.login(page);

    await kemi.gotoRoot(`?propertyId=${propertyId}&filter=requires-action`);
    await kemi.inventoryRow(expired).waitFor({ state: 'visible', timeout: API_TIMEOUT });
    await expect(page).toHaveURL(/\/chemicals\/inventory\?.*filter=requires-action/, { timeout: UI_TIMEOUT });
    await expect(kemi.propertySelect()).toContainText(property.name, { timeout: UI_TIMEOUT });
    await expect(page.locator('#chemicalsRequiresActionChip')).toHaveAttribute('aria-pressed', 'true', { timeout: UI_TIMEOUT });
    await expect(kemi.inventoryRow(approved)).toHaveCount(0, { timeout: UI_TIMEOUT });
  });

  test('Task 15: "Åbn etiket" opens the seeded label as a PDF; it is disabled without a label', async ({ page }) => {
    test.setTimeout(150000);
    await stubDocumentDownloads(page);
    const kemi = await open(page, 'inventory');

    const approvedMenu = await kemi.openInventoryRowMenu(approved);
    await expect(approvedMenu('chemicalRowLabel')).toBeEnabled({ timeout: UI_TIMEOUT });
    const response = documentResponse(page, 'label', LABEL_MD5);
    const opened = page.context().waitForEvent('page', { timeout: UI_TIMEOUT });
    await clickMenuItem(page, approvedMenu('chemicalRowLabel'), 'the label item');
    const labelResponse = await response;
    expect(labelResponse.status()).toBe(200);
    expect(labelResponse.headers()['content-type']).toContain('application/pdf');
    await (await opened).close();

    const expiredMenu = await kemi.openInventoryRowMenu(expired);
    await expect(expiredMenu('chemicalRowLabel')).toBeDisabled({ timeout: UI_TIMEOUT });
    await page.keyboard.press('Escape');
  });

  test('Task 15: the side panel has a label button that opens the label, and it is disabled without a label', async ({ page }) => {
    test.setTimeout(150000);
    await stubDocumentDownloads(page);
    const kemi = await open(page, 'inventory');

    await (await kemi.openInventoryRowMenu(expired))('chemicalRowDetails').click({ timeout: UI_TIMEOUT });
    await page.locator('#chemicalDrawer').waitFor({ state: 'visible', timeout: UI_TIMEOUT });
    await expect(page.locator('#chemicalDrawerLabelBtn')).toBeDisabled({ timeout: UI_TIMEOUT });
    await page.locator('#chemicalDrawerCloseBtn').click({ timeout: UI_TIMEOUT });
    await page.locator('#chemicalDrawer').waitFor({ state: 'hidden', timeout: UI_TIMEOUT });

    await (await kemi.openInventoryRowMenu(approved))('chemicalRowDetails').click({ timeout: UI_TIMEOUT });
    await page.locator('#chemicalDrawer').waitFor({ state: 'visible', timeout: UI_TIMEOUT });
    await expect(page.locator('#chemicalDrawerLabelBtn')).toBeEnabled({ timeout: UI_TIMEOUT });
    const response = documentResponse(page, 'label', LABEL_MD5);
    await page.locator('#chemicalDrawerLabelBtn').click({ timeout: UI_TIMEOUT });
    expect((await response).headers()['content-type']).toContain('application/pdf');
    await page.locator('#chemicalDrawerCloseBtn').click({ timeout: UI_TIMEOUT });
    await page.locator('#chemicalDrawer').waitFor({ state: 'hidden', timeout: UI_TIMEOUT });
  });

  test('Task 15b: with the user locale en-US the SDS opens in English; "Andre sprog" offers the Danish one', async ({ page }) => {
    test.setTimeout(180000);
    await setAdminLocale('en-US');
    try {
      await stubDocumentDownloads(page);
      const kemi = await open(page, 'inventory');

      const menu = await kemi.openInventoryRowMenu(approved);
      const english = documentResponse(page, 'sds', SDS_EN_MD5);
      await clickMenuItem(page, menu('chemicalRowSds'), 'the SDS item');
      expect((await english).headers()['content-type']).toContain('application/pdf');

      const again = await kemi.openInventoryRowMenu(approved);
      await again('chemicalRowSdsOthers').click({ timeout: UI_TIMEOUT });
      const others = page.locator('.cdk-overlay-container [id^="chemicalRowSdsOther-"]');
      await expect(others).toHaveCount(1, { timeout: UI_TIMEOUT });
      await expect(others.first()).toHaveAttribute('id', /-da$/, { timeout: UI_TIMEOUT });
      const danish = documentResponse(page, 'sds', SDS_DA_MD5);
      await clickMenuItem(page, others.first(), 'the Danish SDS item');
      expect((await danish).headers()['content-type']).toContain('application/pdf');
    } finally {
      await setAdminLocale('da');
    }
  });

  test('side panel -> remove with the hazardous-waste warning', async ({ page }) => {
    test.setTimeout(120000);
    const kemi = await open(page, 'inventory');

    const menuItem = await kemi.openInventoryRowMenu(expired);
    await menuItem('chemicalRowDetails').click({ timeout: UI_TIMEOUT });
    await page.locator('#chemicalDrawer').waitFor({ state: 'visible', timeout: UI_TIMEOUT });
    await expect(page.locator('#chemicalDrawerTitle')).toHaveText(expired, { timeout: UI_TIMEOUT });
    await page.locator('#chemicalDrawerRemoveBtn').click({ timeout: UI_TIMEOUT });
    await page.locator('#chemicalDrawer').waitFor({ state: 'hidden', timeout: UI_TIMEOUT });
    await expect(page.locator('#chemicalRemoveHazardWarning')).toBeVisible({ timeout: UI_TIMEOUT });
    // The radio itself: a click on the empty end of the full-width row selects nothing (Material radio).
    await page.locator('#chemicalRemoveReasonDisposed input[type="radio"]').check({ timeout: UI_TIMEOUT });
    await expect(page.locator('#chemicalRemoveSaveBtn')).toBeEnabled({ timeout: UI_TIMEOUT });
    const removed = kemi.waitForChemicalsCall('POST', /^placements\/\d+\/remove$/);
    await page.locator('#chemicalRemoveSaveBtn').click({ timeout: UI_TIMEOUT });
    await removed;
    await expect(kemi.inventoryRow(expired)).toHaveCount(0, { timeout: API_TIMEOUT });
    await expect(page.locator('#chemicalsActionBanner')).toHaveCount(0, { timeout: UI_TIMEOUT });
  });

  test('move to another location', async ({ page }) => {
    test.setTimeout(120000);
    const kemi = await open(page, 'inventory');
    await expect(kemi.inventoryRow(approved)).toContainText(lade, { timeout: API_TIMEOUT });

    const menuItem = await kemi.openInventoryRowMenu(approved);
    await menuItem('chemicalRowMove').click({ timeout: UI_TIMEOUT });
    await dialogReady(page, page.locator('#chemicalMoveNote'));
    await kemi.selectMtxOption('#chemicalMoveTargetSelect', kemirum);
    await fillInDialog(page, page.locator('#chemicalMoveNote'), 'Shelf 1');
    await expect(page.locator('#chemicalMoveSaveBtn')).toBeEnabled({ timeout: UI_TIMEOUT });
    const moved = kemi.waitForChemicalsCall('POST', /^placements\/\d+\/move$/);
    await page.locator('#chemicalMoveSaveBtn').click({ timeout: UI_TIMEOUT });
    await moved;
    await expect(kemi.inventoryRow(approved)).toContainText(kemirum, { timeout: API_TIMEOUT });
    await expect(kemi.inventoryRow(approved)).toContainText('Shelf 1', { timeout: UI_TIMEOUT });
  });

  test('exports: authority list PDF, Excel of the filter, removed placements', async ({ page }) => {
    test.setTimeout(150000);
    const kemi = await open(page, 'inventory');
    await kemi.inventoryRow(approved).waitFor({ state: 'visible', timeout: API_TIMEOUT });

    // "<title> - <property> - <yyyy-MM-dd>.<ext>": the title is in the user's language, the property name is not.
    const named = (extension: string) => new RegExp(`${property.name} - \\d{4}-\\d{2}-\\d{2}\\.${extension}$`);
    expect(await kemi.exportFromInventory('chemicalsExportAuthorityList')).toMatch(named('pdf'));
    expect(await kemi.exportFromInventory('chemicalsExportInventory')).toMatch(named('xlsx'));
    expect(await kemi.exportFromInventory('chemicalsExportRemoved')).toMatch(named('xlsx'));
  });

  test('W3 refuses to archive a location with an open placement', async ({ page }) => {
    test.setTimeout(90000);
    const kemi = await open(page, 'locations');

    await expect(kemi.locationRow(kemirum).locator('[id^="chemicalLocationArchive-"]')).toBeDisabled({ timeout: API_TIMEOUT });
    await expect(kemi.locationRow(lade).locator('[id^="chemicalLocationArchive-"]')).toBeEnabled({ timeout: UI_TIMEOUT });
  });
});
