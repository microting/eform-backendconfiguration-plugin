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

  // Fill the create event modal
  async fillCreateModal(data: {
    title: string;
    eformName?: string;
    planningTag?: string;
  }): Promise<void> {
    await this.page.locator('#calendarEventTitle').fill(data.title);

    if (data.eformName) {
      const eformSelect = this.page.locator('#calendarEventEform');
      await eformSelect.click();
      await this.page.locator('.ng-dropdown-panel input[type="text"]').fill(data.eformName);
      await this.page.waitForTimeout(500);
      await this.page.locator('.ng-dropdown-panel .ng-option').first().click();
    }

    if (data.planningTag) {
      const tagSelect = this.page.locator('#calendarEventPlanningTag');
      await tagSelect.click();
      await this.page
        .locator('.ng-dropdown-panel .ng-option')
        .filter({ hasText: data.planningTag })
        .click();
    }
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

  // Verify event exists on calendar
  async verifyEventExists(title: string): Promise<boolean> {
    return await this.getEventByTitle(title).isVisible();
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
}
