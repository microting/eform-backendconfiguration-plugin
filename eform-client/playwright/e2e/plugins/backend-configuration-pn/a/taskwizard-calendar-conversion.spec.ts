import { test, expect } from '@playwright/test';
import { LoginPage } from '../../../Page objects/Login.page';
import { CalendarUiEnhancementsPage } from '../calendar-ui-enhancements.page';

/**
 * Regression test for the CalendarConfigurationBackfillService startup
 * conversion of old task-wizard plannings.
 *
 * SEED (appended at the end of the two shard-a SQL dumps):
 *   - items-planning `Plannings` Id 9001: the "Altid" wizard encoding
 *     (RepeatType=0, RepeatEvery=0), StartDate 2026-01-07, plus
 *     PlanningNameTranslation rows.
 *   - backend-configuration `AreaRules` Id 9001 (CreatedInGuide=1, Property 1
 *     "Farm 1", Area 1) + `AreaRuleTranslations` ("GamleOpgaveKonvertering",
 *     da+en) + `AreaRulePlannings` Id 9001 (ItemPlanningId 9001, RepeatType=0,
 *     RepeatEvery=0) with NO CalendarConfiguration row.
 *
 * The shard-a CI job loads the dumps AFTER first boot and then RESTARTS the
 * container, so migrations and the startup backfill both run against the
 * seeded data. The backfill must convert the Altid planning to daily
 * (Day, RepeatEvery=1 on both entities) and create a CalendarConfiguration
 * (StartHour 9, Duration 1) on Farm 1's auto-created "Default" board.
 *
 * ASSERTION MODEL — why "Altid"
 * -----------------------------
 * Altid is the only wizard frequency whose rendering CHANGES on conversion:
 * unconverted, a (0,0) planning renders exactly once, in the week containing
 * its StartDate (2026-01-07), and never in any later week; converted, it
 * renders daily 09:00–10:00 forever. We therefore navigate one week FORWARD
 * (strictly future relative to any CI run date) and assert the event on ALL
 * SEVEN days, then one more week forward to prove the recurrence continues.
 * If the backfill ever stops running, every assertion here fails hard.
 * (A weekly seed would be useless: the legacy null-CSV weekly render path plus
 * the 09:00 no-config fallback make an unconverted weekly row indistinguishable
 * from a converted one.)
 *
 * The seeded rows are converted exactly once at the post-seed restart and are
 * never mutated by this read-only spec, so ordering against other shard-a
 * specs does not matter. All assertions are scoped to the unique title.
 */

const TITLE = 'GamleOpgaveKonvertering';
const PROPERTY_NAME = 'Farm 1';
const ALL_DAYS = [0, 1, 2, 3, 4, 5, 6]; // data-day index: Mon=0 .. Sun=6

test.describe('Task-wizard planning → calendar startup conversion', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('http://localhost:4200');
    await new LoginPage(page).login();
    await page.waitForTimeout(2000);

    const calendarPage = new CalendarUiEnhancementsPage(page);
    await calendarPage.goToCalendar();
    await calendarPage.ensureSidebarOpen();

    // The container auto-selects the first property, which is normally
    // Farm 1 (Id 1) already — click it anyway so the selection is
    // deterministic. Because a re-select of the already-active property may
    // not trigger a fresh /tasks/week round-trip, the response waiter is
    // best-effort (same .catch pattern as o/calendar-repeat-presets.spec.ts).
    const weekResp = page.waitForResponse(
      r => r.url().includes('/api/backend-configuration-pn/calendar/tasks/week')
        && r.request().method() === 'POST',
      { timeout: 15000 }
    );
    await calendarPage.selectProperty(PROPERTY_NAME);
    await weekResp.catch(() => undefined);
    await page.waitForTimeout(1000);
  });

  test('converted Altid wizard planning renders daily at 09:00–10:00', async ({ page }) => {
    test.setTimeout(180000);
    const calendarPage = new CalendarUiEnhancementsPage(page);

    // --- Week +1 (strictly future): the converted Altid planning must render
    // on every day of the week at the backfill's fixed 09:00–10:00 slot.
    // Unconverted, a (0,0) planning renders ONLY in its 2026-01-07 StartDate
    // week, so any block here proves the startup conversion ran. ---
    await calendarPage.navigateToNextWeek();

    for (const day of ALL_DAYS) {
      const count = await calendarPage.getDayColumnTaskBlocks(day, TITLE).count();
      expect(
        count,
        `Expected the converted Altid task-wizard planning "${TITLE}" on ` +
        `data-day ${day} (Mon=0..Sun=6) of the next week — the startup ` +
        `CalendarConfigurationBackfillService must rewrite it to daily — ` +
        `but found ${count} blocks.`
      ).toBeGreaterThanOrEqual(1);
    }

    // Backfill creates the CalendarConfiguration with StartHour=9,
    // Duration=1 → the card's time text must render 09:00–10:00.
    const timeText = await calendarPage.getEventTimeText(TITLE);
    expect(
      timeText,
      `Expected the converted event's time text to start at 09:00 ` +
      `(CalendarConfiguration.StartHour=9 from the backfill), got "${timeText}".`
    ).toContain('09:00');
    expect(
      timeText,
      `Expected the converted event's time text to end at 10:00 ` +
      `(CalendarConfiguration.Duration=1 from the backfill), got "${timeText}".`
    ).toContain('10:00');

    // --- Week +2: the daily recurrence continues — all seven days again. ---
    await calendarPage.navigateToNextWeek();

    for (const day of ALL_DAYS) {
      const count = await calendarPage.getDayColumnTaskBlocks(day, TITLE).count();
      expect(
        count,
        `Expected "${TITLE}" to keep recurring daily in the following week ` +
        `(data-day ${day}), but found ${count} blocks.`
      ).toBeGreaterThanOrEqual(1);
    }

    const timeTextNext = await calendarPage.getEventTimeText(TITLE);
    expect(
      timeTextNext,
      `Expected the recurring occurrence to keep the 09:00 start, got "${timeTextNext}".`
    ).toContain('09:00');
  });
});
