import { Page, Locator } from '@playwright/test';

export class BackendConfigurationTaskTrackerPage {
  constructor(private page: Page) {}

  backendConfigurationPnButton(): Locator {
    return this.page.locator('#backend-configuration-pn');
  }

  backendConfigurationPnTaskTrackerButton(): Locator {
    return this.page.locator('#backend-configuration-pn-task-tracker');
  }

  async goToTaskTracker(): Promise<void> {
    const taskTrackerBtn = this.backendConfigurationPnTaskTrackerButton();
    const isVisible = await taskTrackerBtn.isVisible();
    if (!isVisible) {
      await this.backendConfigurationPnButton().click();
    }
    await taskTrackerBtn.click();
    // Wait for spinner to hide (wait for any loading overlay to disappear)
    await this.page.locator('ngx-spinner').waitFor({ state: 'hidden' }).catch(() => {/* ignore if spinner not present */});
  }

  propertyIdFilter(): Locator {
    return this.page.locator('#propertyIdFilter');
  }

  tagsFilter(): Locator {
    return this.page.locator('#tagsFilter');
  }

  workersFilter(): Locator {
    return this.page.locator('#workersFilter');
  }

  cancelSaveColumns(): Locator {
    return this.page.locator('#cancelSaveColumns');
  }

  saveColumns(): Locator {
    return this.page.locator('#saveColumns');
  }

  columnModalProperty(): Locator {
    return this.page.locator('#property');
  }

  columnModalTask(): Locator {
    return this.page.locator('#task');
  }

  columnModalTags(): Locator {
    return this.page.locator('#tags');
  }

  columnModalWorkers(): Locator {
    return this.page.locator('#changeColumnsBtn');
  }

  columnModalStart(): Locator {
    return this.page.locator('#start');
  }

  columnModalRepeat(): Locator {
    return this.page.locator('#repeat');
  }

  columnModalDeadline(): Locator {
    return this.page.locator('#deadline');
  }

  async rowNum(): Promise<number> {
    return this.page.locator('.mat-row').count();
  }

  getFirstRowObject(): TaskTrackerRowObject {
    return new TaskTrackerRowObject(this.page, 1);
  }

  getRowObjectByNum(num: number): TaskTrackerRowObject {
    return new TaskTrackerRowObject(this.page, num);
  }

  getRowObjects(maxNum: number): TaskTrackerRowObject[] {
    const rowObjects: TaskTrackerRowObject[] = [];
    for (let i = 1; i <= maxNum; i++) {
      rowObjects.push(this.getRowObjectByNum(i));
    }
    return rowObjects;
  }
}

export class TaskTrackerRowObject {
  constructor(
    private page: Page,
    private rowNum: number = 1
  ) {}

  getRowLocator(): Locator {
    return this.page.locator('.mat-row').nth(this.rowNum - 1);
  }

  getRow(rowNum: number): TaskTrackerRowObject {
    return new TaskTrackerRowObject(this.page, rowNum);
  }
}
