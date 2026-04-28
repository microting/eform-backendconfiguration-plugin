import { Page, Locator, expect } from '@playwright/test';

/**
 * Self-contained page object for the calendar UI-enhancements suite under
 * the `r/` directory. Intentionally does not import from `l/` so the two
 * suites stay independently maintainable in the CI matrix.
 *
 * Helpers are thin and locator-only; assertions live in the spec.
 */
export class CalendarUiEnhancementsPage {
  constructor(private page: Page) {}

  // ----- Navigation / property selection -----------------------------------

  async goToCalendar(): Promise<void> {
    await this.page.goto('http://localhost:4200/plugins/backend-configuration-pn/calendar');
    await this.page.waitForTimeout(2000);
    await this.page
      .locator('app-calendar-container')
      .waitFor({ state: 'visible', timeout: 30000 });
  }

  async selectProperty(name: string): Promise<void> {
    await this.page.locator('.property-item').filter({ hasText: name }).click();
    await this.page.waitForTimeout(1000);
  }

  // ----- Calendar slot click ----------------------------------------------

  /**
   * Click an empty time slot on the week-grid. dayOffset 0 = first visible
   * day (Monday in the default week view), hour 0..23.
   *
   * The grid snaps to 30-min boundaries; clicking the CENTER of an hour
   * band lands on H:30, not H:00. We click 5 px into the top of the band
   * to guarantee the H:00 slot, which makes the modal's default start a
   * clean integer hour. Tests downstream depend on this for their math.
   */
  async clickEmptyTimeSlot(dayOffset: number, hour: number): Promise<void> {
    const dayCell = this.page.locator(`.day-cell-content[data-day="${dayOffset}"]`);
    const box = await dayCell.boundingBox();
    if (!box) throw new Error(`Day cell ${dayOffset} not found`);
    const hourHeight = 52;
    const y = box.y + hour * hourHeight + 5;
    const x = box.x + box.width / 2;
    await this.page.mouse.click(x, y);
    await this.page.waitForTimeout(500);
  }

  /**
   * Full sequence: navigate to the next week (so today's column is in the
   * future and slot clicks are accepted), click Monday@9AM, wait for the
   * create modal title input to appear.
   */
  async openCreateModalAt9AM(): Promise<void> {
    return this.openCreateModalAtSlot(0, 9);
  }

  /**
   * Variant that lets the caller pick the day-of-week (0=Mon..6=Sun) and
   * hour, so multiple tests in the same describe block can each create
   * events at distinct slots without colliding with previously-created
   * events on the same week.
   */
  async openCreateModalAtSlot(dayOffset: number, hour: number): Promise<void> {
    // Advance one week to guarantee we click a future slot.
    await this.page.locator('mat-icon:has-text("chevron_right")').first().click();
    await this.page.waitForTimeout(1500);
    await this.clickEmptyTimeSlot(dayOffset, hour);
    await this.page
      .locator('#calendarEventTitle')
      .waitFor({ state: 'visible', timeout: 15000 });
    await this.page.waitForTimeout(300);
  }

  async closeEventModal(): Promise<void> {
    // Mini-picker overlay (if open) has a CDK backdrop that intercepts
    // clicks aimed at elements behind it, including the cancel button.
    // Press Escape first to dismiss any open overlay, then click cancel.
    if (await this.getMiniPickerOverlay().count() > 0) {
      await this.page.keyboard.press('Escape');
      await this.getMiniPickerOverlay().waitFor({ state: 'detached', timeout: 2000 }).catch(() => undefined);
    }
    await this.page.locator('#calendarEventCancelBtn').click();
    await this.page.waitForTimeout(500);
  }

  // ----- Time-field helpers (mtx-select / ng-select) -----------------------

  // mtx-select renders the typeable input with role="combobox". `.ng-input`
  // is NOT what we want here — see plan §"Locator for mtx-select input".
  getStartTimeInput(): Locator {
    return this.page.locator('.gcal-time-field').first().locator('input[role="combobox"]');
  }

  getEndTimeInput(): Locator {
    return this.page.locator('.gcal-time-field').nth(1).locator('input[role="combobox"]');
  }

  /**
   * Focus → click → type → wait for the dropdown panel → press the commit
   * key. The waitFor is mandatory: pressing Enter before the panel renders
   * triggers ng-select's "no items" fallback and silently drops the entry.
   */
  async typeStartTime(text: string, key: 'Enter' | 'Tab' = 'Enter'): Promise<void> {
    await this.typeInTimeField(this.getStartTimeInput(), text, key);
  }

  async typeEndTime(text: string, key: 'Enter' | 'Tab' = 'Enter'): Promise<void> {
    await this.typeInTimeField(this.getEndTimeInput(), text, key);
  }

  private async typeInTimeField(input: Locator, text: string, key: 'Enter' | 'Tab'): Promise<void> {
    await input.click();
    await input.focus();
    // Clear any prior search term in the input before typing — without this,
    // re-using the same select (e.g. typing a second value back-to-back)
    // would APPEND rather than replace.
    await this.page.keyboard.press('Control+A');
    await this.page.keyboard.press('Delete');
    // Use type() rather than fill() — mtx-select reads keyboard events
    // for the addTag / typeahead path. fill() would set value without
    // dispatching the input events ng-select listens to.
    await input.type(text, { delay: 30 });
    await this.page
      .locator('.ng-dropdown-panel')
      .waitFor({ state: 'visible', timeout: 1000 });
    // Wait until ng-select has finished filtering and marked an option (an
    // addTag pseudo-option OR a matching preset). Without this, Tab/Enter
    // can fire before the marked-item state catches up and the keystroke
    // becomes a no-op.
    await this.page
      .locator('.ng-dropdown-panel .ng-option-marked, .ng-dropdown-panel .ng-option:has(.ng-tag-label)')
      .first()
      .waitFor({ state: 'visible', timeout: 1500 })
      .catch(() => undefined);
    await this.page.keyboard.press(key);
    // Small settle so the duration-preserve subscriber and the
    // setValue() round-trip both finish before the assertion reads.
    await this.page.waitForTimeout(250);
  }

  async getStartTimeValue(): Promise<string> {
    const label = this.page.locator('.gcal-time-field').first().locator('.ng-value-label');
    if ((await label.count()) === 0) return '';
    return ((await label.first().textContent()) ?? '').trim();
  }

  async getEndTimeValue(): Promise<string> {
    const label = this.page.locator('.gcal-time-field').nth(1).locator('.ng-value-label');
    if ((await label.count()) === 0) return '';
    return ((await label.first().textContent()) ?? '').trim();
  }

  /**
   * Open the end-time mtx-select dropdown (panel is portaled via
   * `appendTo="body"`), click the option matching the given time, then
   * dismiss the panel by clicking the title input.
   */
  async setEndTimeFromDropdown(time: string): Promise<void> {
    await this.getEndTimeInput().click();
    await this.page
      .locator('.ng-dropdown-panel')
      .waitFor({ state: 'visible', timeout: 5000 });
    const option = this.page
      .locator('.ng-dropdown-panel .ng-option')
      .filter({ hasText: new RegExp(`^${time}$`) })
      .first();
    await option.click();
    await this.page.waitForTimeout(200);
    // Click the modal title input to close the dropdown without
    // mutating any other state.
    await this.page.locator('#calendarEventTitle').click();
    await this.page.waitForTimeout(150);
  }

  // ----- Mini-calendar overlay --------------------------------------------

  /**
   * Click the calendar-icon button next to the Date input in the event
   * create/edit modal. The button is an Angular Material icon-button
   * containing `<mat-icon>calendar_today</mat-icon>`; match by that icon
   * to avoid dependence on Material's internal class names.
   */
  async openEventDatePicker(): Promise<void> {
    const button = this.page
      .locator('.gcal-date-field button')
      .filter({ has: this.page.locator('mat-icon', { hasText: 'calendar_today' }) })
      .first();
    await button.click();
    await this.getMiniPickerOverlay().waitFor({ state: 'visible', timeout: 5000 });
  }

  /**
   * Click the calendar-icon button in the custom-repeat dialog (Ends → On).
   */
  async openCustomRepeatDatePicker(): Promise<void> {
    const button = this.page
      .locator('.custom-repeat-dialog .end-option .date-input button')
      .filter({ has: this.page.locator('mat-icon', { hasText: 'calendar_today' }) })
      .first();
    // Date input button is [disabled]="endMode !== 'until'"; wait for the
    // disabled attribute to drop after the radio click flips endMode.
    await button.waitFor({ state: 'visible', timeout: 5000 });
    await this.page.waitForFunction(
      (el: Element) => !(el as HTMLButtonElement).disabled,
      await button.elementHandle(),
      { timeout: 5000 }
    );
    await button.click();
    await this.getMiniPickerOverlay().waitFor({ state: 'visible', timeout: 5000 });
  }

  // The mini-picker is portaled out of the modal via cdkConnectedOverlay,
  // so it MUST be located at page scope, never modal-scoped.
  getMiniPickerOverlay(): Locator {
    return this.page.locator('.mini-picker-overlay-card');
  }

  async getMiniCalendarMonthLabel(): Promise<string> {
    return ((await this.page.locator('.mini-picker-overlay-card .month-label').textContent()) ?? '').trim();
  }

  async clickMiniCalendarNext(): Promise<void> {
    await this.page
      .locator('.mini-picker-overlay-card .cal-header button')
      .nth(1)
      .click();
    await this.page.waitForTimeout(150);
  }

  async clickMiniCalendarPrev(): Promise<void> {
    await this.page
      .locator('.mini-picker-overlay-card .cal-header button')
      .nth(0)
      .click();
    await this.page.waitForTimeout(150);
  }

  async getMiniCalendarWeekNumbers(): Promise<number[]> {
    const cells = this.page.locator('.mini-picker-overlay-card .week-num-cell');
    const count = await cells.count();
    const out: number[] = [];
    for (let i = 0; i < count; i++) {
      const txt = ((await cells.nth(i).textContent()) ?? '').trim();
      out.push(parseInt(txt, 10));
    }
    return out;
  }

  // ----- Header / sidebar --------------------------------------------------

  async clickPropertyPill(): Promise<void> {
    await this.page.locator('.property-pill').click();
    // Brief settle so the sidebar transition can flip the class.
    await this.page.waitForTimeout(150);
  }

  /**
   * The menu-toggle button — the leading button in `.calendar-header`
   * containing a `<mat-icon>menu</mat-icon>`. Filtering by the icon text
   * keeps this stable even if more icon-buttons are added to the header
   * later.
   */
  getMenuToggleButton(): Locator {
    return this.page
      .locator('.calendar-header button')
      .filter({ has: this.page.locator('mat-icon', { hasText: 'menu' }) })
      .first();
  }

  async clickMenuToggleButton(): Promise<void> {
    await this.getMenuToggleButton().click();
    await this.page.waitForTimeout(150);
  }

  async isSidebarClosed(): Promise<boolean> {
    return (await this.page.locator('.calendar-shell.sidebar-closed').count()) > 0;
  }

  async ensureSidebarOpen(): Promise<void> {
    if (await this.isSidebarClosed()) {
      await this.clickMenuToggleButton();
      await this.page.waitForTimeout(150);
    }
  }

  // ----- Resize gesture ----------------------------------------------------

  /**
   * Find a task block by visible title substring. Use .first() because the
   * preview popover may also render a copy of the block.
   */
  findEventBlock(title: string): Locator {
    return this.page.locator('.task-block').filter({ hasText: title }).first();
  }

  /**
   * Read the visible time text on a task block (e.g. "09:00 – 10:00"). The
   * `.task-time` element only renders when duration ≥ 0.5 h — for very
   * short events this returns empty.
   */
  async getEventTimeText(title: string): Promise<string> {
    const block = this.findEventBlock(title);
    const time = block.locator('.task-time');
    if ((await time.count()) === 0) return '';
    return ((await time.textContent()) ?? '').trim();
  }

  /**
   * Drag a resize handle on a task block. `edge` is which edge to grab
   * (top = changes start time, bottom = changes end/duration). `deltaPx`
   * is signed: positive moves down, negative up.
   *
   * Hover the block first so the handles render visibly (they exist in
   * the DOM regardless, but Playwright's actionability check is happier
   * when they're visible).
   *
   * Registers the post-resize reload waiter BEFORE issuing mouse.up so
   * the listener can never miss the response — registering after the
   * gesture races the network round-trip and times out on quick CI
   * boxes (D1 hit this pre-fix). For recurring events that pop the
   * scope modal, pass `awaitReload: false` and the caller handles the
   * scope click + reload separately.
   */
  async dragResizeHandle(
    title: string,
    edge: 'top' | 'bottom',
    deltaPx: number,
    options: { awaitReload?: boolean } = {}
  ): Promise<void> {
    const { awaitReload = true } = options;
    const block = this.findEventBlock(title);
    await block.hover();
    const box = await block.boundingBox();
    if (!box) throw new Error(`Task block "${title}" not found`);

    // Handles are 6 px tall, positioned ENTIRELY INSIDE the block
    // (top:0 / bottom:0). Aim at the vertical centre (box.y + 3 for
    // top, box.y + height - 3 for bottom) for a generous hit margin.
    const startX = box.x + box.width / 2;
    const startY = edge === 'top' ? box.y + 3 : box.y + box.height - 3;

    await this.page.mouse.move(startX, startY);
    await this.page.mouse.down();
    // Move in steps so the resize component's mousemove subscriber fires
    // intermediate updates (matches the existing drag pattern in l/).
    await this.page.mouse.move(startX, startY + deltaPx, { steps: 8 });
    await this.page.waitForTimeout(150);

    // Pre-register the reload waiter so it cannot miss the response.
    const reloadWait = awaitReload
      ? this.page.waitForResponse(
          r => r.url().includes('/api/backend-configuration-pn/calendar/tasks/week')
            && r.request().method() === 'POST',
          { timeout: 30000 }
        )
      : null;

    await this.page.mouse.up();

    if (reloadWait) {
      await reloadWait;
      await this.page.waitForTimeout(500);
    } else {
      await this.page.waitForTimeout(500);
    }
  }

  /**
   * Set the event-modal Repeat dropdown to "Weekly on {weekday}". The
   * repeat options have stable order (calendar-repeat.service:245+):
   * 0:none, 1:daily, 2:weeklyOne, 3:weeklyAll, 4:monthlyDom, 5:yearlyOne,
   * 6:custom. Index 2 is what we want. Pick by position to avoid
   * locale-dependent label matching.
   *
   * The repeat select is [searchable]="false", so click .ng-select-container
   * (the inner combobox input is non-interactive at opacity 0).
   */
  async setRepeatToWeekly(): Promise<void> {
    const repeatRow = this.page
      .locator('.gcal-row')
      .filter({ has: this.page.locator('mat-icon.gcal-icon:has-text("sync")') });
    await repeatRow.locator('.ng-select-container').first().click();
    await this.page.locator('.ng-dropdown-panel').waitFor({ state: 'visible', timeout: 5000 });
    await this.page.locator('.ng-dropdown-panel .ng-option').nth(2).click();
    await this.page.waitForTimeout(300);
  }

  /**
   * Drag a task block (move gesture, not resize) from its current slot
   * to a new day-of-week + hour. cdkDrag activates only when the drag
   * starts on the .task-block-body (which carries cdkDragHandle), so
   * we mouse-down at the block's vertical centre — not on the resize
   * handles at the top/bottom edges.
   *
   * Pre-registers the post-move reload waiter BEFORE mouse.up; pass
   * { awaitReload: false } when the caller will handle the scope-modal
   * flow and the eventual reload separately.
   */
  async dragEventToSlot(
    title: string,
    targetDayOffset: number,
    targetHour: number,
    options: { awaitReload?: boolean } = {},
  ): Promise<void> {
    const { awaitReload = true } = options;
    const block = this.findEventBlock(title);
    await block.hover();
    const box = await block.boundingBox();
    if (!box) throw new Error(`Task block "${title}" not found`);

    // Centre of the block — well inside the body (cdkDragHandle), away
    // from the 6 px resize handles at the top and bottom edges.
    const startX = box.x + box.width / 2;
    const startY = box.y + box.height / 2;

    const targetCell = this.page.locator(`.day-cell-content[data-day="${targetDayOffset}"]`);
    const targetBox = await targetCell.boundingBox();
    if (!targetBox) throw new Error(`Target day cell ${targetDayOffset} not found`);
    const hourHeight = 52;
    const targetX = targetBox.x + targetBox.width / 2;
    // 5 px into the hour band — same convention as clickEmptyTimeSlot
    // so we land cleanly on H:00 (the grid snaps to 30-min boundaries).
    const targetY = targetBox.y + targetHour * hourHeight + 5;

    await this.page.mouse.move(startX, startY);
    await this.page.mouse.down();
    // Step through so cdkDrag's drag-detection threshold fires.
    await this.page.mouse.move(targetX, targetY, { steps: 12 });
    await this.page.waitForTimeout(200);

    const reloadWait = awaitReload
      ? this.page.waitForResponse(
          r => r.url().includes('/api/backend-configuration-pn/calendar/tasks/week')
            && r.request().method() === 'POST',
          { timeout: 30000 }
        )
      : null;

    await this.page.mouse.up();

    if (reloadWait) {
      await reloadWait;
    }
    await this.page.waitForTimeout(500);
  }

  /**
   * Read the parent day-cell's `data-day` attribute (0=Mon..6=Sun) for
   * the event with the given title. Returns -1 if the event is not on
   * the calendar grid.
   */
  async getEventDayIndex(title: string): Promise<number> {
    const block = this.findEventBlock(title);
    if ((await block.count()) === 0) return -1;
    const dayParent = block.locator(
      'xpath=ancestor::*[contains(concat(" ", normalize-space(@class), " "), " day-cell-content ")]'
    ).first();
    const dayAttr = await dayParent.getAttribute('data-day');
    return dayAttr ? parseInt(dayAttr, 10) : -1;
  }

  /**
   * Pick a scope in the RepeatScopeModalComponent that pops after a
   * resize on a recurring event. Clicks the matching radio + Confirm.
   */
  async pickScopeInModal(scope: 'this' | 'thisAndFollowing' | 'all'): Promise<void> {
    const dialog = this.page.locator('app-repeat-scope-modal');
    await dialog.waitFor({ state: 'visible', timeout: 10000 });
    await dialog.locator(`mat-radio-button[value="${scope}"]`).click();
    await this.page.waitForTimeout(150);
    // The confirm button is the second action button; matches by class
    // since the visible label is locale-translated.
    await dialog.locator('button.btn-primary').click();
    await this.page.waitForTimeout(800);
  }

  /**
   * Click the calendar's previous-week chevron, wait for the week-tasks
   * POST so the new week's data is loaded before assertions.
   */
  async navigateToPreviousWeek(): Promise<void> {
    const wait = this.page.waitForResponse(
      r => r.url().includes('/api/backend-configuration-pn/calendar/tasks/week')
        && r.request().method() === 'POST',
      { timeout: 30000 }
    );
    await this.page.locator('mat-icon:has-text("chevron_left")').first().click();
    await wait;
    await this.page.waitForTimeout(800);
  }

  async navigateToNextWeek(): Promise<void> {
    const wait = this.page.waitForResponse(
      r => r.url().includes('/api/backend-configuration-pn/calendar/tasks/week')
        && r.request().method() === 'POST',
      { timeout: 30000 }
    );
    await this.page.locator('mat-icon:has-text("chevron_right")').first().click();
    await wait;
    await this.page.waitForTimeout(800);
  }

  /**
   * Advance the schedule (list) / day view by `days` days. The chevron
   * step in non-week views is 1 day per click (see
   * calendar-container.component.ts:421), so a 1-week advance needs 7
   * clicks. Each click awaits the /tasks/week POST so navigation is
   * deterministic.
   */
  async navigateScheduleByDays(days: number): Promise<void> {
    for (let i = 0; i < days; i++) {
      await this.navigateToNextWeek();
    }
  }

  // ----- Schedule (list) view ---------------------------------------------

  /**
   * Switch the calendar from week view to the schedule (list) view via the
   * view-mode mtx-select in the header. Index 2 = "List" (per
   * calendar-header.component.ts:28-32 viewModeOptions order: Day, Week, List).
   */
  async switchToScheduleView(): Promise<void> {
    // Open the view-mode select. It's the only mtx-select inside the
    // header's .text-field--rounded form-field.
    const viewSelect = this.page.locator('.text-field--rounded mtx-select .ng-select-container').first();
    await viewSelect.click();
    await this.page.locator('.ng-dropdown-panel').waitFor({ state: 'visible', timeout: 5000 });
    // Index 2 is List per the modal's viewModeOptions order.
    await this.page.locator('.ng-dropdown-panel .ng-option').nth(2).click();
    await this.page.locator('.schedule-view').waitFor({ state: 'visible', timeout: 5000 });
    await this.page.waitForTimeout(300);
  }

  /**
   * Find a clickable schedule-view row by visible title substring.
   */
  findScheduleItem(title: string): Locator {
    return this.page.locator('.schedule-item').filter({ hasText: title }).first();
  }

  /** Edit button in the preview popover (week-grid + schedule both reuse it). */
  getPreviewEditButton(): Locator {
    return this.page.locator('#calendarEventEditBtn');
  }

  /** Copy/Duplicate button in the preview popover. */
  getPreviewCopyButton(): Locator {
    return this.page.locator('#calendarEventCopyBtn');
  }

  /**
   * Delete button in the preview popover. Has no id; identify by the
   * `delete` mat-icon child.
   */
  getPreviewDeleteButton(): Locator {
    return this.page
      .locator('app-task-preview-modal button')
      .filter({ has: this.page.locator('mat-icon', { hasText: 'delete' }) })
      .first();
  }

  // ----- Sticky day-of-week header (week + day views) ----------------------

  /**
   * The actual scrolling element of the calendar grid (week or day view —
   * both are rendered via `app-calendar-week-grid`). The sticky header
   * resolves its containing block to this element after the SCSS fix that
   * disables `overflow: hidden` on the mtx-grid host.
   */
  getWeekGridWrapper(): Locator {
    return this.page.locator('.week-grid-wrapper');
  }

  /**
   * The day-of-week strip at the top of the grid ("man. 27/04 / I dag /
   * ons. 29/04 / …"). Both the week-view and day-view branches in
   * calendar-container.html mount `app-calendar-week-grid`, so we scope to
   * that selector. `.first()` is defensive in case mtx-table renders an
   * additional internal header row.
   */
  getHeaderRow(): Locator {
    return this.page.locator('app-calendar-week-grid .mat-mdc-header-row').first();
  }

  /**
   * Switch the calendar to day view via the view-mode mtx-select in the
   * header. viewModeOptions order is fixed in calendar-header.component.ts
   * ngOnInit: 0=Day, 1=Week, 2=List — so day view is index 0. Using the
   * positional index keeps the helper locale-agnostic (the visible labels
   * are translated, e.g. "Dag" in Danish).
   */
  async switchToDayView(): Promise<void> {
    const viewSelect = this.page.locator('.text-field--rounded mtx-select .ng-select-container').first();
    await viewSelect.click();
    await this.page.locator('.ng-dropdown-panel').waitFor({ state: 'visible', timeout: 5000 });
    // Index 0 = Day per viewModeOptions order in calendar-header.component.ts.
    await this.page.locator('.ng-dropdown-panel .ng-option').nth(0).click();
    // Day view also renders <app-calendar-week-grid>, just with dayViewMode=true.
    await this.page.locator('app-calendar-week-grid').waitFor({ state: 'visible', timeout: 5000 });
    await this.page.waitForTimeout(300);
  }

  /**
   * Verify the day-of-week header row stays visually pinned to the top of
   * the scrolling `.week-grid-wrapper` after a vertical scroll. Two modes:
   *   - 'partial': scrollTop = 200
   *   - 'max'    : scrollTop = scrollHeight (clamped by the browser to the
   *                max scrollable offset)
   *
   * Hard-fails if the wrapper isn't actually scrollable enough to make the
   * test meaningful — without this, a tall CI viewport could falsely pass
   * because there is no overflow to scroll past.
   */
  async assertHeaderStaysSticky(scrollAmount: 'partial' | 'max'): Promise<void> {
    const wrapper = this.getWeekGridWrapper();
    const headerRow = this.getHeaderRow();

    const wrapperBoxBefore = await wrapper.boundingBox();
    if (!wrapperBoxBefore) {
      throw new Error('Sticky-header check: .week-grid-wrapper has no bounding box (not visible).');
    }
    const metrics = await wrapper.evaluate(el => ({
      clientHeight: (el as HTMLElement).clientHeight,
      scrollHeight: (el as HTMLElement).scrollHeight,
    }));
    const overflow = metrics.scrollHeight - metrics.clientHeight;
    if (overflow <= 50) {
      throw new Error(
        `Sticky-header check is not meaningful: .week-grid-wrapper is not scrollable enough ` +
        `(scrollHeight=${metrics.scrollHeight}, clientHeight=${metrics.clientHeight}, ` +
        `overflow=${overflow}px). Increase the viewport's height in the Playwright config or ` +
        `rerun with a shorter window so the time grid actually overflows.`
      );
    }

    // Apply the requested scroll. Reading offsetHeight forces a synchronous
    // layout flush so the subsequent boundingBox read sees the post-scroll
    // position rather than the pre-scroll one.
    const targetScroll = scrollAmount === 'partial' ? 200 : metrics.scrollHeight;
    await wrapper.evaluate((el, n) => {
      (el as HTMLElement).scrollTop = n;
      // eslint-disable-next-line @typescript-eslint/no-unused-expressions
      (el as HTMLElement).offsetHeight;
    }, targetScroll);

    const wrapperBoxAfter = await wrapper.boundingBox();
    const headerBox = await headerRow.boundingBox();
    if (!wrapperBoxAfter || !headerBox) {
      throw new Error('Sticky-header check: missing bounding box after scroll.');
    }

    // Sanity: the wrapper itself shouldn't move during scroll (it's anchored
    // by the calendar-main flex layout). If it does, the sticky comparison
    // becomes meaningless — surface that as a clear failure rather than a
    // misleading sticky regression.
    expect(
      wrapperBoxAfter.y,
      `Wrapper itself moved during scroll (before=${wrapperBoxBefore.y}, ` +
      `after=${wrapperBoxAfter.y}); sticky comparison is invalid.`
    ).toBeCloseTo(wrapperBoxBefore.y, 0);

    const delta = Math.abs(headerBox.y - wrapperBoxAfter.y);
    expect(
      delta,
      `Header row drifted from wrapper top after ${scrollAmount} scroll: ` +
      `headerBox.y=${headerBox.y}, wrapperBox.y=${wrapperBoxAfter.y}, delta=${delta}px ` +
      `(expected < 2px — sticky regressed).`
    ).toBeLessThan(2);
  }
}
