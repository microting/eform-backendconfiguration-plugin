/**
 * Bounded-wait helpers for the backend-configuration Playwright suite.
 *
 * Retries are forbidden in this suite (see playwright.config.ts), so the run is
 * the only signal we get. Every wait therefore carries an explicit timeout: an
 * unbounded wait silently inherits the *test* timeout, which turns a broken
 * selector or a request that never fires into a 10-15 minute hang that reports
 * nothing useful. See CLAUDE.md ("Playwright tests fail fast").
 */
import { Page, Response } from '@playwright/test';

/** A local UI transition settles: dialog opens/closes, menu panel appears. */
export const UI_TIMEOUT = 15000;

/** One backend round-trip completes (create / delete / list refresh). */
export const API_TIMEOUT = 30000;

/**
 * One backend round-trip that provisions through the Microting SDK completes.
 * Device-user creation talks to an external service, so it is legitimately
 * slower than a plain CRUD call.
 */
export const SLOW_API_TIMEOUT = 60000;

/**
 * `page.waitForResponse` with a mandatory timeout and a failure message that
 * names the response we were waiting for. Playwright's own timeout message is
 * just `waiting for event "response"`, which does not say which call went
 * missing.
 */
export async function waitForApiResponse(
  page: Page,
  description: string,
  predicate: (response: Response) => boolean,
  timeout: number = API_TIMEOUT
): Promise<Response> {
  try {
    return await page.waitForResponse(predicate, { timeout });
  } catch (error) {
    throw new Error(
      `Timed out after ${timeout}ms waiting for ${description} — the request was never observed. ` +
        `(${(error as Error).message})`
    );
  }
}
