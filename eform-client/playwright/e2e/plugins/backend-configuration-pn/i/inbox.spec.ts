import { test, expect, Locator, Page } from '@playwright/test';
import { LoginPage } from '../../../Page objects/Login.page';
import { generateRandmString } from '../../../helper-functions';
import DatabaseConfigurationConstants from '../../../Constants/DatabaseConfigurationConstants';
import { BackendConfigurationPropertiesPage, PropertyCreateUpdate } from '../BackendConfigurationProperties.page';
import {
  BackendConfigurationArchivePage, DEFAULT_FOLDER, expectSuccess, FILES_PATH, InboxStatus,
} from '../BackendConfigurationArchive.page';
import {
  addressTokenHash,
  announceInboxDocument,
  CUSTOMER_NO,
  expectSigned,
  fetchCatalog,
  HubStub,
  seedInboxDocument,
  startHubStub,
} from '../inbox-hub-seed';
import { API_TIMEOUT, UI_TIMEOUT } from '../wait-helpers';

/** The CI admin's e-mail, used as an ordinary sender: every sender is accepted unless blocked. */
const ADMIN_EMAIL: string = DatabaseConfigurationConstants.email;

/**
 * Indbakke (inbound mail inbox), tenant side (shard i). Documents are seeded through the signed hub
 * endpoints with the CI-only key, exactly as the central service would; the central service's own API
 * is a stub on the runner host (see inbox-hub-seed.ts), so address creation and rotation really go out
 * and are checked for their signature.
 *
 * I1 a delivered document with a property suggestion is filed and shows in Arkiv.
 * I2 a document from a sender nobody configured is Ready; it is rejected and its sender blocked, so the
 *    sender's next mail is refused.
 * I3 a document without suggestions cannot be filed without a property, and is rejected.
 * I4 the blocked senders are listed, saved and survive a reload; the address is rotated.
 * I5 a document reviewed by Microting shows no "checked by Microting" marker.
 * I6 a folder and a date suggestion: the folder is preselected, Godkend approves the date, the filing carries both.
 *
 * The admin sees the archive section tabs (the inbox is admin-only).
 * It is also the tenant's first user (lowest AspNetUsers Id), the only one who may rotate the address.
 */
test.describe.serial('Indbakke', () => {
  let page: Page;
  let hub: HubStub;
  let archive: BackendConfigurationArchivePage;
  const property: PropertyCreateUpdate = {
    name: `Inbox ${generateRandmString(5)}`,
    chrNumber: generateRandmString(5),
    address: 'Nordvej 12, 8000 Aarhus C',
    cvrNumber: '1111111',
  };
  let propertyId = 0;
  /** Blocked in I2 and expected on the settings page in I4. */
  const blockedSender = `scanner-${generateRandmString(4).toLowerCase()}@example.net`;

  test.beforeAll(async ({ browser }) => {
    // Login (up to ~2 min on a cold app, see LoginPage.login) plus one property create and one catalog call.
    test.setTimeout(240000);
    hub = await startHubStub();
    page = await browser.newPage();
    archive = new BackendConfigurationArchivePage(page);
    await page.goto('http://localhost:4200', { timeout: API_TIMEOUT });
    await new LoginPage(page).login();
    const properties = new BackendConfigurationPropertiesPage(page);
    await properties.goToProperties();
    await properties.createProperty(property);
    // The id the central service would suggest: from the signed catalog, as the service itself gets it.
    const catalog = await fetchCatalog(page.request);
    const listed = catalog.properties.filter(p => p.name === property.name);
    expect(listed, `the catalog lists the property "${property.name}" exactly once`).toHaveLength(1);
    propertyId = listed[0].id;
  });

  test.afterAll(async () => {
    await page?.close();
    await hub?.close();
  });

  const rowFor = (fileName: string): Locator => archive.inboxRow(fileName);
  const dialog = (): Locator => archive.dialog();

  /** Arms the wait for the call, clicks, and expects the call to succeed. */
  async function clickAndExpectSuccess(target: Locator, method: string, pathSuffix: string, what: string): Promise<void> {
    const call = archive.waitForCall(what, method, pathSuffix);
    await target.click({ timeout: UI_TIMEOUT });
    await expectSuccess(call, what);
  }

  test('I1 file a delivered document into the archive', async () => {
    // Two signed seeding calls, three page loads and the file call, each bounded by API_TIMEOUT.
    test.setTimeout(120000);
    const fileName = `Servicerapport ${generateRandmString(4)}.pdf`;
    await seedInboxDocument(page.request, {
      fromAddress: ADMIN_EMAIL, fileName, suggestions: [{ kind: 'property', targetId: propertyId }],
    });
    await archive.openInbox();
    for (const tab of ['#archiveNavInbox', '#archiveNavArchive', '#archiveNavSettings']) {
      await expect(page.locator(tab), `archive section tab ${tab}`).toBeVisible({ timeout: UI_TIMEOUT });
    }

    const id = await archive.inboxRowId(fileName, InboxStatus.Ready);
    await archive.openReview(id);

    // The suggestion (confidence 0.5) is preselected, so the property requirement is already met.
    const choice = dialog().locator(`button.inbox-choice[data-kind="property"][data-target-id="${propertyId}"]`);
    await expect(choice).toHaveAttribute('aria-pressed', 'true', { timeout: UI_TIMEOUT });
    // No folder suggestion: filing waits until a folder is chosen (D9).
    await expect(dialog().locator('#inboxFolderRequired')).toBeVisible({ timeout: UI_TIMEOUT });
    await expect(dialog().locator('#inboxFileBtn')).toBeDisabled({ timeout: UI_TIMEOUT });
    await archive.chooseInSelect('inboxFolderSelect', DEFAULT_FOLDER);
    await expect(dialog().locator('#inboxFileBtn')).toBeEnabled({ timeout: UI_TIMEOUT });

    await archive.fileFromReview(id, 'filing the document');
    await archive.closeReview();
    await expect(rowFor(fileName)).toHaveAttribute('data-status', InboxStatus.Filed, { timeout: UI_TIMEOUT });

    await clickAndExpectSuccess(page.locator('#archiveNavArchive'), 'POST', FILES_PATH, 'loading the archive');
    // Filed into the default folder, which the Arkiv page opens on, under the PDF's own name.
    const archived = archive.fileRow(fileName);
    await expect(archived).toHaveCount(1, { timeout: UI_TIMEOUT });
    await expect(archived).toContainText(property.name!, { timeout: UI_TIMEOUT });
  });

  test('I2 reject a document and block its sender', async () => {
    // Two signed seeding calls, two page loads, the reject call and one more "arrived", each bounded by API_TIMEOUT.
    test.setTimeout(120000);
    const fileName = `scan_${generateRandmString(4)}.pdf`;
    // Nobody configured this sender: it is accepted all the same.
    await seedInboxDocument(page.request, { fromAddress: blockedSender, fileName });
    const id = await archive.openReadyReview(fileName);

    const rejected = archive.waitForCall('rejecting the document and blocking its sender', 'POST', `/inbox/${id}/reject`);
    await archive.clickInDialog('inboxRejectBlockBtn');
    await expectSuccess(rejected, 'rejecting the document and blocking its sender');
    expect(new URL((await rejected).url()).searchParams.get('block'), 'the reject asked for the block').toBe('true');
    await expect(dialog()).toBeHidden({ timeout: UI_TIMEOUT });
    await expect(rowFor(fileName)).toHaveCount(0, { timeout: UI_TIMEOUT });

    // The sender's next mail is refused (any letter case) and never reaches the list.
    const nextFileName = `scan_${generateRandmString(4)}.pdf`;
    const next = await announceInboxDocument(page.request, blockedSender.toUpperCase(), nextFileName);
    expect(next.senderVerdict, 'the blocked sender is refused').toBe('blocked');
    await archive.openInbox();
    await expect(rowFor(nextFileName)).toHaveCount(0, { timeout: UI_TIMEOUT });
  });

  test('I3 reject a document', async () => {
    // Two signed seeding calls, one page load and the reject call, each bounded by API_TIMEOUT.
    test.setTimeout(120000);
    const fileName = `Faktura ${generateRandmString(4)}.pdf`;
    await seedInboxDocument(page.request, { fromAddress: ADMIN_EMAIL, fileName });
    const id = await archive.openReadyReview(fileName);

    // No suggestion, so no property: filing is blocked until one is chosen.
    await expect(dialog().locator('#inboxPropertyRequired')).toBeVisible({ timeout: UI_TIMEOUT });
    await expect(dialog().locator('#inboxFolderRequired')).toBeVisible({ timeout: UI_TIMEOUT });
    await expect(dialog().locator('#inboxFileBtn')).toBeDisabled({ timeout: UI_TIMEOUT });

    await clickAndExpectSuccess(dialog().locator('#inboxRejectBtn'), 'POST', `/inbox/${id}/reject`, 'rejecting the document');
    await expect(dialog()).toBeHidden({ timeout: UI_TIMEOUT });
    // The default view leaves rejected documents out.
    await expect(rowFor(fileName)).toHaveCount(0, { timeout: UI_TIMEOUT });
  });

  test('I4 settings: save blocked senders and rotate the address', async () => {
    // Three settings loads (the first registers the address with the stub hub), save and rotate.
    test.setTimeout(120000);
    await archive.openInbox();
    await clickAndExpectSuccess(page.locator('#archiveNavSettings'), 'GET', '/inbox/settings', 'loading the inbox settings');

    const address = page.locator('#inboxAddress');
    const blockedInput = page.locator('#inboxBlockedInput');
    const addressPath = `/api/tenants/${CUSTOMER_NO}/address`;
    const shape = new RegExp(`^${CUSTOMER_NO}-[a-z2-7]{10}@indbakke\\.microting\\.dk$`);
    await expect(address).toHaveValue(shape, { timeout: UI_TIMEOUT });
    const first = await address.inputValue();
    const registered = hub.callsTo('PUT', addressPath);
    expect(registered, 'the first address was registered with the hub once').toHaveLength(1);
    expect(registered[0].body.tokenHash).toBe(addressTokenHash(first));
    expectSigned(registered[0], 'the address registration');

    // The sender blocked with the rejection in I2 is listed; there is nothing to allow any more.
    await expect(blockedInput).toHaveValue(blockedSender, { timeout: UI_TIMEOUT });
    await expect(page.locator('#inboxRulesInput')).toHaveCount(0);
    await blockedInput.fill('@example.org');
    await clickAndExpectSuccess(page.locator('#inboxSaveBtn'), 'PUT', '/inbox/settings', 'saving the inbox settings');

    const reloaded = archive.waitForCall('inbox settings after reload', 'GET', '/inbox/settings');
    await page.reload({ timeout: API_TIMEOUT });
    await expectSuccess(reloaded, 'reloading the inbox settings');
    // The save is a full replace: the sender blocked in I2 is gone, only the new rule is left.
    await expect(blockedInput).toHaveValue('@example.org', { timeout: UI_TIMEOUT });
    await expect(address).toHaveValue(first, { timeout: UI_TIMEOUT });

    // Only the first user (this login) sees "Lav ny adresse". Rotating asks first (window.confirm)
    // because the old address stops after the grace period.
    page.once('dialog', d => d.accept());
    await clickAndExpectSuccess(page.locator('#inboxRotateBtn'), 'POST', '/inbox/settings/rotate-address',
      'rotating the address');
    await expect(address).not.toHaveValue(first, { timeout: UI_TIMEOUT });
    await expect(address).toHaveValue(shape, { timeout: UI_TIMEOUT });
    const second = await address.inputValue();

    const rotation = hub.callsTo('PUT', addressPath);
    expect(rotation, 'the rotation was registered with the hub').toHaveLength(2);
    expect(rotation[1].body.tokenHash).toBe(addressTokenHash(second));
    expect(rotation[1].body.previousTokenHash).toBe(addressTokenHash(first));
    expectSigned(rotation[1], 'the address rotation');
  });

  test('I5 a document reviewed by Microting shows no "checked by Microting" marker', async () => {
    // Two signed seeding calls and one page load, each bounded by API_TIMEOUT.
    test.setTimeout(120000);
    const fileName = `Servicerapport ${generateRandmString(4)}.pdf`;
    await seedInboxDocument(page.request, { fromAddress: ADMIN_EMAIL, fileName, reviewedByMicroting: true });
    // The removed texts in Danish and English (the e2e UI language).
    await archive.openInbox();
    const id = await archive.inboxRowId(fileName, InboxStatus.Ready);
    await expect(rowFor(fileName).getByText(/Kontrolleret af Microting|Checked by Microting/)).toHaveCount(0, { timeout: UI_TIMEOUT });

    await archive.openReview(id);
    // The dialog body has rendered (its file button is there) before checking what it does not show.
    await expect(dialog().locator('#inboxFileBtn')).toBeVisible({ timeout: UI_TIMEOUT });
    await expect(dialog().getByText(/Microting har kontrolleret|Microting checked these suggestions/)).toHaveCount(0, { timeout: UI_TIMEOUT });
  });

  test('I6 a folder and a date suggestion: the folder is preselected, Godkend approves the date', async () => {
    // One catalog call, two signed seeding calls, one page load and the file call, each bounded by API_TIMEOUT.
    test.setTimeout(120000);
    const fileName = `Faktura ${generateRandmString(4)}.pdf`;
    const catalog = await fetchCatalog(page.request);
    const arkiv = catalog.folders.find(f => f.name === DEFAULT_FOLDER && f.parentId === null);
    expect(arkiv, 'the catalog lists the default folder').toBeTruthy();
    await seedInboxDocument(page.request, {
      fromAddress: ADMIN_EMAIL,
      fileName,
      suggestions: [
        { kind: 'property', targetId: propertyId },
        { kind: 'folder', targetId: arkiv!.id },
        { kind: 'documentDate', targetDate: '2026-10-03' },
      ],
    });
    const id = await archive.openReadyReview(fileName);

    // Confidence 0.5 = the preselect threshold, as for properties.
    const folderChoice = dialog().locator(`button.inbox-choice[data-kind="folder"][data-target-id="${arkiv!.id}"]`);
    await expect(folderChoice).toHaveAttribute('aria-pressed', 'true', { timeout: UI_TIMEOUT });
    await expect(dialog().locator('#inboxDateValue')).toContainText('03.10.2026', { timeout: UI_TIMEOUT });
    await archive.clickInDialog('inboxDateApproveBtn');
    await expect(dialog().locator('#inboxDateApproved')).toBeVisible({ timeout: UI_TIMEOUT });

    const filed = await archive.fileFromReview(id, 'filing with the folder and the approved date');
    expect(filed.request().postDataJSON(), 'the filing carries the folder and the approved date').toEqual(
      expect.objectContaining({ folderId: arkiv!.id, documentDate: '2026-10-03', documentDateApproved: true }));
  });
});
