import { Page, Locator } from '@playwright/test';

export class CalendarPage {
  constructor(private page: Page) {}

  // Navigation - navigate directly to calendar URL
  async goToCalendar(): Promise<void> {
    await this.page.goto('http://localhost:4200/plugins/backend-configuration-pn/calendar');
    await this.page.waitForTimeout(2000);
    // Wait for the calendar container to render
    await this.page.locator('app-calendar-container').waitFor({ state: 'visible', timeout: 30000 });
  }

  // Sidebar - select property by name
  async selectProperty(name: string): Promise<void> {
    await this.page.locator('.property-item').filter({ hasText: name }).click();
    await this.page.waitForTimeout(1000);
  }

  // Click a time slot on the calendar grid.
  // dayOffset: 0 = first visible day, 1 = next day, etc.
  // hour: 0-23
  async clickTimeSlot(dayOffset: number, hour: number): Promise<void> {
    const dayCell = this.page.locator(`.day-cell-content[data-day="${dayOffset}"]`);
    const box = await dayCell.boundingBox();
    if (!box) throw new Error(`Day cell ${dayOffset} not found`);
    const hourHeight = 52;
    const y = box.y + hour * hourHeight + hourHeight / 2;
    const x = box.x + box.width / 2;
    await this.page.mouse.click(x, y);
    await this.page.waitForTimeout(500);
  }

  // Fill the create event modal. Always selects the first Report headline
  // (planning tag) option and the first available assignee because the
  // backend requires both.
  async fillCreateModal(data: {
    title: string;
    eformName?: string;
  }): Promise<void> {
    await this.page.locator('#calendarEventTitle').fill(data.title);

    if (data.eformName) {
      const eformSelect = this.page.locator('#calendarEventEform');
      await eformSelect.click();
      await this.page.locator('.ng-dropdown-panel input[type="text"]').fill(data.eformName);
      await this.page.waitForTimeout(500);
      await this.page.locator('.ng-dropdown-panel .ng-option').first().click();
    }

    // Pick the first Report headline option — backend requires it.
    const tagSelect = this.page.locator('#calendarEventPlanningTag');
    await tagSelect.click();
    await this.page.waitForTimeout(500);
    await this.page.locator('.ng-dropdown-panel .ng-option').first().click();
    await this.page.waitForTimeout(300);

    // Pick the first assignee — backend rejects events with no sites.
    const assigneeSelect = this.page.locator('#calendarEventAssignee');
    await assigneeSelect.click();
    await this.page.locator('.ng-dropdown-panel').waitFor({ state: 'visible', timeout: 10000 });
    await this.page.locator('.ng-dropdown-panel .ng-option').first().click();
    // Close the multi-select dropdown by clicking outside it.
    await this.page.locator('#calendarEventTitle').click();
    await this.page.waitForTimeout(300);
  }

  // Save the modal
  async saveModal(): Promise<void> {
    await this.page.locator('#calendarEventSaveBtn').click();
    await this.page.waitForTimeout(2000);
  }

  // Find an event by title on the calendar
  getEventByTitle(title: string): Locator {
    return this.page.locator('app-calendar-task-block').filter({ hasText: title });
  }

  // Wait for an event tile to appear on the calendar. Events render async
  // after the property is selected (GET /tasks/week fires, then Angular
  // re-renders the week grid), so a sync .isVisible() check would race
  // that render. Returns true if the event becomes visible within the
  // timeout, false otherwise — never throws.
  async waitForEvent(title: string, timeout = 10000): Promise<boolean> {
    try {
      await this.getEventByTitle(title).waitFor({ state: 'visible', timeout });
      return true;
    } catch {
      return false;
    }
  }

  // Drag an event to a new day/time
  async dragEvent(title: string, targetDayOffset: number, targetHour: number): Promise<void> {
    const eventEl = this.getEventByTitle(title);
    const eventBox = await eventEl.boundingBox();
    if (!eventBox) throw new Error(`Event "${title}" not found`);

    const targetCell = this.page.locator(`.day-cell-content[data-day="${targetDayOffset}"]`);
    const targetBox = await targetCell.boundingBox();
    if (!targetBox) throw new Error(`Target day ${targetDayOffset} not found`);

    const hourHeight = 52;
    const targetY = targetBox.y + targetHour * hourHeight + hourHeight / 2;
    const targetX = targetBox.x + targetBox.width / 2;

    await eventEl.hover();
    await this.page.mouse.down();
    // Move in steps so CDK drag detects it as a real drag
    await this.page.mouse.move(targetX, targetY, { steps: 10 });
    await this.page.waitForTimeout(200);
    await this.page.mouse.up();
    await this.page.waitForTimeout(1000);
  }

  // Click an existing event to open the preview popover
  async openEventPreview(title: string): Promise<void> {
    await this.getEventByTitle(title).click();
    await this.page.waitForTimeout(1000);
  }

  // Click the Copy button in the preview modal to open the create-edit modal in copy mode
  async clickCopyInPreview(): Promise<void> {
    await this.page.locator('#calendarEventCopyBtn').click();
    await this.page.waitForTimeout(1000);
  }

  // Fill a title override in the create-edit modal (for copy flows)
  async overrideTitle(title: string): Promise<void> {
    const titleInput = this.page.locator('#calendarEventTitle');
    await titleInput.fill(title);
    await this.page.waitForTimeout(200);
  }

  // Read the current title value (used to verify the "Copy of" prefix)
  async getCreateModalTitle(): Promise<string> {
    return (await this.page.locator('#calendarEventTitle').inputValue()) || '';
  }

  // Click "Show details" / "Hide details" toggle on the eForm preview
  async toggleEformPreview(): Promise<void> {
    await this.page.locator('#calendarEformPreviewToggle').click();
    await this.page.waitForTimeout(500);
  }

  // True if the preview body is rendered (i.e. expanded)
  async isEformPreviewExpanded(): Promise<boolean> {
    return await this.page.locator('#calendarEformPreviewBody').isVisible();
  }

  // Count the field rows in the expanded preview body
  async countEformPreviewFields(): Promise<number> {
    return await this.page.locator('#calendarEformPreviewBody .eform-field-row').count();
  }

  // Read the selected value from a mtx-select / ng-select by its parent id
  async getSelectValue(selectorId: string): Promise<string> {
    const el = this.page.locator(`${selectorId} .ng-value-label`);
    if (await el.count() === 0) return '';
    return (await el.first().textContent()) || '';
  }

  // Read multi-select values (assignees, tags)
  async getMultiSelectValues(selectorId: string): Promise<string[]> {
    const labels = this.page.locator(`${selectorId} .ng-value-label`);
    const count = await labels.count();
    const values: string[] = [];
    for (let i = 0; i < count; i++) {
      values.push((await labels.nth(i).textContent()) || '');
    }
    return values;
  }

  // Close the preview popover (click the X)
  async closePreview(): Promise<void> {
    await this.page.locator('#calendarEventCancelBtn').click();
    await this.page.waitForTimeout(500);
  }
}
