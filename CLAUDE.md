# CLAUDE.md — eform-backendconfiguration-plugin

## Playwright tests fail fast

The e2e suite is slow, serialised (`workers: 1`) and fanned out over ~26 CI shards. It is the
only signal we get on a change, so it has to fail in seconds with a message that names the
cause — never hang and then say nothing.

1. **Every wait carries an explicit timeout.** An unbounded `waitForResponse` / `waitForRequest` /
   `locator.waitFor()` silently inherits the *test* timeout, so a request that never fires burns
   the whole budget before reporting. `closeCreatePropertyModal()` did exactly this and hung three
   specs for 10–15 minutes each. Use the shared budgets in
   `eform-client/playwright/e2e/plugins/backend-configuration-pn/wait-helpers.ts`
   (`UI_TIMEOUT` 15s, `API_TIMEOUT` 30s, `SLOW_API_TIMEOUT` 60s for SDK-backed calls).
2. **Wait for a response through `waitForApiResponse(page, description, predicate, timeout)`.**
   Playwright's own timeout says `waiting for event "response"`; the wrapper says *which* call
   never arrived.
3. **No `page.waitForTimeout()` standing in for a condition.** A sleep is a guess: too short and
   it is flaky, too long and it wastes minutes across 26 shards. Assert the post-condition instead —
   the dialog is `hidden`, the toggle is `aria-checked="true"`, the row count dropped by one.
4. **Retries are FORBIDDEN** (`retries: 0` in `eform-client/playwright.config.ts`). A retry hides
   the defect it papers over and doubles an already slow run. Never add `retries`, `test.retry()`,
   soft assertions or a retry wrapper — wanting one means you have found a bug to fix.
5. **Never weaken an assertion to get green.** If a shard is red, either the product is broken or
   the test is; both are worth knowing.
6. **Locators are scoped to their container. Avoid bare `.first()` and `{ force: true }`.**
   An unscoped `.first()` can resolve to another row's stale element, and `force: true` skips the
   actionability check that would have said so — that is how the device-user delete button failed
   in `k/time-registration-dashboard-visibility.spec.ts`.
7. **Angular Material menus are not in their row.** `mat-menu` content is projected into a CDK
   overlay on `<body>`, so scope menu items to `.cdk-overlay-container` and address them by the
   row's `action-items-<i>` index — `openRowActionMenu()` in
   `eform-client/playwright/e2e/plugins/backend-configuration-pn/row-action-menu.ts` does both.
   Scoping them to the row finds nothing at all.
8. **`test.setTimeout()` states a budget, not a hiding place.** Size it to the work the test
   actually does and say why in a comment. A 10-minute ceiling is a smell that an inner wait is
   unbounded.
9. **Tests run in CI only, never locally on a dev machine.** Local runs need the frontend, items-
   planning and time-planning checkouts merged plus MariaDB/RabbitMQ containers; verify with `gh`.
10. **Classify a red shard before blaming your change.** A failure in `Wait for app`,
    `Start rabbitmq`, `DB Configuration` or `yarn install` is infrastructure; only a failure inside
    the `<shard> playwright test` step with a named spec is a test failure.
