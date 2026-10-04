import { test, expect, Locator, Page, Response } from '@playwright/test';
import { LoginPage } from '../../../Page objects/Login.page';
import { generateRandmString } from '../../../helper-functions';
import DatabaseConfigurationConstants from '../../../Constants/DatabaseConfigurationConstants';
import { BackendConfigurationPropertiesPage, PropertyCreateUpdate } from '../BackendConfigurationProperties.page';
import {
  addressTokenHash,
  CUSTOMER_NO,
  deliverInboxDocument,
  expectSigned,
  fetchCatalog,
  HubStub,
  seedInboxDocument,
  startHubStub,
} from '../inbox-hub-seed';
import { API_TIMEOUT, UI_TIMEOUT, ignoreUnhandledRejections, waitForApiResponse } from '../wait-helpers';

/**
 * The CI admin's e-mail. The database-configuration step creates the admin with it, so a mail from it
 * counts as an allowed sender (every active user may send) without any sender rule.
 */
const ADMIN_EMAIL: string = DatabaseConfigurationConstants.email;

/** InboxDocumentStatus, as the list renders it in `data-status`. */
const Status = { Preparing: '0', SenderPending: '1', Ready: '2', Filed: '4' } as const;

/**
 * Indbakke (inbound mail inbox), tenant side (shard i). Documents are seeded through the signed hub
 * endpoints with the CI-only key, exactly as the central service would; the central service's own API
 * is a stub on the runner host (see inbox-hub-seed.ts), so address creation, rotation and sender
 * decisions really go out and are checked for their signature.
 *
 * I1 a delivered document with a property suggestion is filed and shows in Arkiv.
 * I2 an unknown sender is approved; the PDF delivered afterwards makes the document Ready.
 * I3 a document without suggestions cannot be filed without a property, and is rejected.
 * I4 the allowed senders are saved and survive a reload; the address is rotated.
 *
 * The admin has every plugin permission, inbox_enable included, so the archive section tabs must show.
 */
test.describe.serial('Indbakke', () => {
  let page: Page;
  let hub: HubStub;
  const property: PropertyCreateUpdate = {
    name: `Inbox ${generateRandmString(5)}`,
    chrNumber: generateRandmString(5),
    address: 'Nordvej 12, 8000 Aarhus C',
    cvrNumber: '1111111',
  };
  let propertyId = 0;

  test.beforeAll(async ({ browser }) => {
    // Login (up to ~2 min on a cold app, see LoginPage.login) plus one property create and one catalog call.
    test.setTimeout(240000);
    hub = await startHubStub();
    page = await browser.newPage();
    await page.goto('http://localhost:4200');
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

  const rowFor = (fileName: string): Locator => page.locator('#inboxTable tr.inbox-row', { hasText: fileName });
  const dialog = (): Locator => page.locator('mat-dialog-container');

  /** Starts waiting for an inbox API call; the wait is armed before the action that triggers it. */
  function waitForInbox(description: string, method: string, pathSuffix: string): Promise<Response> {
    const wait = waitForApiResponse(page, `${method} ${description}`,
      r => r.request().method() === method && new URL(r.url()).pathname.endsWith(pathSuffix), API_TIMEOUT);
    ignoreUnhandledRejections(wait);
    return wait;
  }

  /**
   * The call answered 2xx with `success: true`. A non-2xx fails with its status and body (the cause);
   * OperationResult answers HTTP 200 also on failure, so `success` is checked as well.
   */
  async function expectSuccess(response: Promise<Response>, what: string): Promise<void> {
    const resp = await response;
    if (!resp.ok()) {
      throw new Error(`${what}: HTTP ${resp.status()} ${await resp.text()}`);
    }
    const body = await resp.json();
    expect(body.success, `${what}: ${body.message ?? ''}`).toBe(true);
  }

  /** Arms the wait for the inbox call, clicks, and expects the call to succeed. */
  async function clickAndExpectSuccess(target: Locator, method: string, pathSuffix: string, what: string): Promise<void> {
    const call = waitForInbox(what, method, pathSuffix);
    await target.click();
    await expectSuccess(call, what);
  }

  async function openReview(id: string): Promise<void> {
    await page.locator(`#inboxReviewBtn-${id}`).click();
    await expect(dialog()).toHaveCount(1, { timeout: UI_TIMEOUT });
  }

  async function openInbox(): Promise<void> {
    const listed = waitForInbox('inbox list', 'GET', '/api/backend-configuration-pn/inbox');
    await page.goto('http://localhost:4200/plugins/backend-configuration-pn/files/inbox');
    await expectSuccess(listed, 'loading the inbox list');
    await expect(page.locator('#inboxTable')).toBeVisible({ timeout: UI_TIMEOUT });
  }

  /** The row's inbox id, once exactly one row shows the file in the expected status. */
  async function rowId(fileName: string, status: string): Promise<string> {
    const row = rowFor(fileName);
    await expect(row).toHaveCount(1, { timeout: UI_TIMEOUT });
    await expect(row).toHaveAttribute('data-status', status, { timeout: UI_TIMEOUT });
    const id = await row.getAttribute('data-inbox-id');
    expect(id, `inbox id of the row for ${fileName}`).toMatch(/^\d+$/);
    return id!;
  }

  test('I1 file a delivered document into the archive', async () => {
    // Two signed seeding calls, three page loads and the file call, each bounded by API_TIMEOUT.
    test.setTimeout(120000);
    const fileName = `Servicerapport ${generateRandmString(4)}.pdf`;
    await seedInboxDocument(page.request, {
      fromAddress: ADMIN_EMAIL, fileName, suggestions: [{ kind: 'property', targetId: propertyId }],
    });
    await openInbox();
    for (const tab of ['#archiveNavInbox', '#archiveNavArchive', '#archiveNavSettings']) {
      await expect(page.locator(tab), `archive section tab ${tab}`).toBeVisible({ timeout: UI_TIMEOUT });
    }

    const id = await rowId(fileName, Status.Ready);
    await openReview(id);

    // The suggestion (confidence 0.5) is preselected, so the property requirement is already met.
    const choice = dialog().locator(`button.inbox-choice[data-kind="property"][data-target-id="${propertyId}"]`);
    await expect(choice).toHaveAttribute('aria-pressed', 'true', { timeout: UI_TIMEOUT });
    await expect(dialog().locator('#inboxFileBtn')).toBeEnabled({ timeout: UI_TIMEOUT });

    await clickAndExpectSuccess(dialog().locator('#inboxFileBtn'), 'POST', `/inbox/${id}/file`, 'filing the document');
    await expect(dialog().locator('#inboxUndoBtn')).toBeVisible({ timeout: UI_TIMEOUT });
    await dialog().locator('#inboxCloseBtn').click();
    await expect(dialog()).toBeHidden({ timeout: UI_TIMEOUT });
    await expect(rowFor(fileName)).toHaveAttribute('data-status', Status.Filed, { timeout: UI_TIMEOUT });

    await clickAndExpectSuccess(page.locator('#archiveNavArchive'), 'POST', '/api/backend-configuration-pn/files',
      'loading the archive');
    // Archive file names render as "<name>.<extension>"; the inbox filed it under the PDF's own name.
    const archived = page.locator('app-files-table .mat-mdc-row').filter({
      has: page.locator('.fileName', { hasText: fileName }),
    });
    await expect(archived).toHaveCount(1, { timeout: UI_TIMEOUT });
    await expect(archived).toContainText(property.name!, { timeout: UI_TIMEOUT });
  });

  test('I2 approve an unknown sender', async () => {
    // Arrived, approve (one stub hub round-trip), deliver and two page loads, each bounded by API_TIMEOUT.
    test.setTimeout(120000);
    const fileName = `scan_${generateRandmString(4)}.pdf`;
    const hubDocumentId = await seedInboxDocument(page.request, {
      fromAddress: `scanner-${generateRandmString(4)}@example.net`, fileName, deliver: false,
    });
    await openInbox();
    const id = await rowId(fileName, Status.SenderPending);

    await clickAndExpectSuccess(page.locator(`#inboxApproveSenderBtn-${id}`), 'POST', `/inbox/${id}/approve-sender`,
      'approving the sender');
    // The PDF has not arrived yet, so the approved document is still being prepared.
    await expect(rowFor(fileName)).toHaveAttribute('data-status', Status.Preparing, { timeout: UI_TIMEOUT });

    const decision = hub.callsTo('POST', `/api/tenants/${CUSTOMER_NO}/documents/${hubDocumentId}/sender-decision`);
    expect(decision, 'the hub was told the decision exactly once').toHaveLength(1);
    expect(decision[0].body).toEqual({ decision: 'approve' });
    expectSigned(decision[0], 'the sender decision');

    await deliverInboxDocument(page.request, hubDocumentId, fileName);
    await openInbox();
    await rowId(fileName, Status.Ready);
  });

  test('I3 reject a document', async () => {
    // Two signed seeding calls, one page load and the reject call, each bounded by API_TIMEOUT.
    test.setTimeout(120000);
    const fileName = `Faktura ${generateRandmString(4)}.pdf`;
    await seedInboxDocument(page.request, { fromAddress: ADMIN_EMAIL, fileName });
    await openInbox();
    const id = await rowId(fileName, Status.Ready);
    await openReview(id);

    // No suggestion, so no property: filing is blocked until one is chosen.
    await expect(dialog().locator('#inboxPropertyRequired')).toBeVisible({ timeout: UI_TIMEOUT });
    await expect(dialog().locator('#inboxFileBtn')).toBeDisabled({ timeout: UI_TIMEOUT });

    await clickAndExpectSuccess(dialog().locator('#inboxRejectBtn'), 'POST', `/inbox/${id}/reject`, 'rejecting the document');
    await expect(dialog()).toBeHidden({ timeout: UI_TIMEOUT });
    // The default view leaves rejected documents out.
    await expect(rowFor(fileName)).toHaveCount(0, { timeout: UI_TIMEOUT });
  });

  test('I4 settings: save allowed senders and rotate the address', async () => {
    // Three settings loads (the first registers the address with the stub hub), save and rotate.
    test.setTimeout(120000);
    await openInbox();
    await clickAndExpectSuccess(page.locator('#archiveNavSettings'), 'GET', '/inbox/settings', 'loading the inbox settings');

    const address = page.locator('#inboxAddress');
    const rulesInput = page.locator('#inboxRulesInput');
    const addressPath = `/api/tenants/${CUSTOMER_NO}/address`;
    const shape = new RegExp(`^${CUSTOMER_NO}-[a-z2-7]{10}@indbakke\\.microting\\.dk$`);
    await expect(address).toHaveValue(shape, { timeout: UI_TIMEOUT });
    const first = await address.inputValue();
    const registered = hub.callsTo('PUT', addressPath);
    expect(registered, 'the first address was registered with the hub once').toHaveLength(1);
    expect(registered[0].body.tokenHash).toBe(addressTokenHash(first));
    expectSigned(registered[0], 'the address registration');

    await rulesInput.fill('@example.org');
    await clickAndExpectSuccess(page.locator('#inboxSaveBtn'), 'PUT', '/inbox/settings', 'saving the inbox settings');

    const reloaded = waitForInbox('inbox settings after reload', 'GET', '/inbox/settings');
    await page.reload();
    await expectSuccess(reloaded, 'reloading the inbox settings');
    // The save is a full replace: the sender approved in I2 is gone, only the new rule is left.
    await expect(rulesInput).toHaveValue('@example.org', { timeout: UI_TIMEOUT });
    await expect(address).toHaveValue(first, { timeout: UI_TIMEOUT });

    // Rotating asks first (window.confirm) because the old address stops after the grace period.
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
});
