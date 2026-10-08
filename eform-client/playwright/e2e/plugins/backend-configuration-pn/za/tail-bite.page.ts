import { expect, Locator, Page, Response } from '@playwright/test';
import { API_TIMEOUT, ignoreUnhandledRejections, UI_TIMEOUT, waitForApiResponse } from '../wait-helpers';

const BASE_URL = 'http://localhost:4200';
const API = '/api/backend-configuration-pn/tail-bite';

export type TailBiteTab = 'outbreaks' | 'locations' | 'rules' | 'action-types';
const TAB_IDS: Record<TailBiteTab, string> = {
  outbreaks: 'Outbreaks',
  locations: 'Locations',
  rules: 'Rules',
  'action-types': 'ActionTypes',
};

/** Page object for the tail-bite (halebid) web admin under /plugins/backend-configuration-pn/tail-bite. */
export class TailBitePage {
  constructor(private page: Page) {}

  /** Opens the tail-bite area (it lands on the first enabled property), picks `propertyName` and opens `tab`. */
  async goto(tab: TailBiteTab, propertyName: string): Promise<void> {
    await this.page.goto(`${BASE_URL}/plugins/backend-configuration-pn/tail-bite`);
    await this.page.locator('#tailBitePropertySelect').waitFor({ state: 'visible', timeout: API_TIMEOUT });
    await this.pick('tailBitePropertySelect', propertyName);
    await this.page.locator(`#tailBiteTab${TAB_IDS[tab]}`).click();
    await this.page.waitForURL(new RegExp(`/tail-bite/\\d+/${tab}$`), { timeout: API_TIMEOUT });
  }

  /** Waits for the tail-bite call `method` + `pathPattern` (a regex on the URL path after .../tail-bite/) and asserts success. */
  async expectApi(method: string, pathPattern: RegExp, action: () => Promise<unknown>): Promise<Response> {
    const response = waitForApiResponse(
      this.page,
      `${method} tail-bite ${pathPattern}`,
      (r) => r.request().method() === method && r.url().includes(API) && pathPattern.test(r.url().split(API)[1].split('?')[0]),
      API_TIMEOUT,
    );
    ignoreUnhandledRejections(response);
    await action();
    const res = await response;
    const body = await res.json();
    expect(body.success, `${method} ${res.url()} -> ${body.message}`).toBe(true);
    return res;
  }

  /** Opens an mtx-select by id and picks the option whose text is exactly `text`. No .first(): duplicate option text should fail on strictness. */
  async pick(selectId: string, text: string): Promise<void> {
    await this.page.locator(`#${selectId}`).click({ timeout: UI_TIMEOUT });
    const panel = this.page.locator('.ng-dropdown-panel');
    await panel.waitFor({ state: 'visible', timeout: UI_TIMEOUT });
    await panel.locator('.ng-option').filter({ hasText: exactText(text) }).click({ timeout: UI_TIMEOUT });
  }

  locationRow(name: string): Locator {
    return this.page.locator('#tailBiteLocationsTable tr[id^="tailBiteLocationRow-"]').filter({
      has: this.page.locator('td.mat-column-name', { hasText: exactText(name) }),
    });
  }

  async selectLocation(name: string): Promise<void> {
    await this.locationRow(name).click();
    await expect(this.page.locator('#tailBiteSelectedName')).toHaveText(name, { timeout: UI_TIMEOUT });
  }

  /** Types into the text dialog (a name or a reason) and saves. */
  async answerTextDialog(text: string): Promise<void> {
    await this.page.locator('#tailBiteTextDialogInput').fill(text);
    await this.page.locator('#tailBiteTextDialogSave').click();
  }

  /** Confirms the platform delete modal through its confirm button id. */
  async confirm(confirmId: string): Promise<void> {
    await this.page.locator(`#${confirmId}`).click();
  }

  async screenshot(name: string): Promise<void> {
    await this.page.screenshot({ path: `playwright-results/tail-bite/${name}.png`, fullPage: true });
  }
}

export function escapeRegExp(text: string): string {
  return text.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}

function exactText(text: string): RegExp {
  return new RegExp(`^\\s*${escapeRegExp(text)}\\s*$`);
}
