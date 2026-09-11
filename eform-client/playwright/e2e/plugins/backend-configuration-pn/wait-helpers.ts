/**
 * Bounded-wait helpers for the backend-configuration Playwright suite.
 *
 * Retries are forbidden in this suite (see playwright.config.ts), so the run is
 * the only signal we get. Every wait therefore carries an explicit timeout: an
 * unbounded wait silently inherits the *test* timeout, which turns a broken
 * selector or a request that never fires into a 10-15 minute hang that reports
 * nothing useful. See CLAUDE.md ("Playwright tests fail fast").
 */
import { errors, Page, Request, Response, Route } from '@playwright/test';

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
 * missing. The timeout has no default on purpose: picking a budget is the point
 * of this helper.
 */
export async function waitForApiResponse(
  page: Page,
  description: string,
  predicate: (response: Response) => boolean,
  timeout: number
): Promise<Response> {
  try {
    return await page.waitForResponse(predicate, { timeout });
  } catch (error) {
    // Only a timeout means "the call never arrived". A closed page/context, or a
    // predicate that threw, must keep its own message — relabelling those would
    // send the reader hunting for a missing request that was never the problem.
    if (!(error instanceof errors.TimeoutError)) {
      throw error;
    }
    throw new Error(
      `Timed out after ${timeout}ms waiting for ${description} — the request was never observed. ` +
        `(${String(error)})`
    );
  }
}

/**
 * Attaches a no-op handler to waits that are awaited later — or, on an error
 * path, never. A bounded wait can reject before the code reaches its `await`,
 * and a rejection nobody has handled yet fails the whole run; the later `await`
 * still observes the failure.
 */
export function ignoreUnhandledRejections(...pending: Promise<unknown>[]): void {
  for (const wait of pending) {
    wait.catch(() => undefined);
  }
}

/**
 * `page.waitForRequest` with a mandatory timeout and a failure message that
 * names the request we were waiting for — the request-side twin of
 * {@link waitForApiResponse}.
 */
export async function waitForApiRequest(
  page: Page,
  description: string,
  predicate: (request: Request) => boolean,
  timeout: number
): Promise<Request> {
  try {
    return await page.waitForRequest(predicate, { timeout });
  } catch (error) {
    if (!(error instanceof errors.TimeoutError)) {
      throw error;
    }
    throw new Error(
      `Timed out after ${timeout}ms waiting for ${description} — the request was never issued. ` +
        `(${String(error)})`
    );
  }
}

/** A GET held at the network layer by {@link holdApiGetRequests}. */
export interface HeldApiRequest {
  /** Resolves once the first matching request has been issued — and is therefore being held. */
  held: Promise<Request>;
  /**
   * Lets every held matching request through; later matching requests pass
   * straight through too. Safe to call more than once.
   */
  release: () => Promise<void>;
}

/**
 * Holds every GET whose URL path ends with `pathSuffix` until `release()` is
 * called, so a test can assert what the page shows WHILE that request is in
 * flight — deterministically, instead of racing it. This is not a sleep: nothing
 * waits on the clock; the response is simply not delivered until the test says so.
 *
 * Install it immediately before the action that fires the request: `held` is a
 * bounded wait whose `timeout` runs from here. Always call `release()` in a
 * `finally`, so a failing assertion cannot leave the page stuck on a request
 * that is never answered.
 *
 * `release()` deliberately does NOT unroute. Removing a route handler while one
 * of its routes is still in flight makes Playwright continue that route itself,
 * racing the handler's own `route.continue()`: in CI the handler lost with
 * "route.continue: Route is already handled!" and the released request was never
 * delivered to the page. Once released, the handler is a pass-through that lives
 * as long as the page — one test — so exactly one party ever handles each route.
 */
export async function holdApiGetRequests(
  page: Page,
  description: string,
  pathSuffix: string,
  timeout: number
): Promise<HeldApiRequest> {
  const matchesPath = (url: URL) => url.pathname.endsWith(pathSuffix);
  let releaseHeld!: () => void;
  const released = new Promise<void>(resolve => {
    releaseHeld = resolve;
  });
  const handler = async (route: Route) => {
    if (route.request().method() !== 'GET') {
      await route.fallback();
      return;
    }
    await released;
    await route.continue();
  };

  const held = waitForApiRequest(
    page,
    description,
    request => request.method() === 'GET' && matchesPath(new URL(request.url())),
    timeout
  );
  // Awaited by the caller after its triggering action, which can throw first.
  ignoreUnhandledRejections(held);
  await page.route(matchesPath, handler);

  return {
    held,
    release: async () => {
      releaseHeld();
    },
  };
}
