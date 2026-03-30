import { Page, Locator } from '@playwright/test';
import { selectValueInNgSelector, selectDateRangeOnNewDatePicker } from '../../../helper-functions';

export class BackendConfigurationReportsPage {
  constructor(private page: Page) {}

  backendConfigurationPnButton(): Locator {
    return this.page.locator('#backend-configuration-pn');
  }

  backendConfigurationPnReportsButton(): Locator {
    return this.page.locator('#backend-configuration-pn-reports');
  }

  async goToReports(): Promise<void> {
    const reportsBtn = this.backendConfigurationPnReportsButton();
    const isVisible = await reportsBtn.isVisible();
    if (!isVisible) {
      await this.backendConfigurationPnButton().click();
    }
    await reportsBtn.click();
    await this.page.locator('app-backend-configuration-pn-report').waitFor({ state: 'visible' });
  }

  async rowNum(): Promise<number> {
    return this.page.locator('.mat-row').count();
  }

  tagSelector(): Locator {
    return this.page.locator('#tagSelector');
  }

  dateFormInput(): Locator {
    return this.page.locator('mat-date-range-input');
  }

  generateTableBtn(): Locator {
    return this.page.locator('#generateTableBtn');
  }

  generateWordBtn(): Locator {
    return this.page.locator('#generateWordBtn');
  }

  generateExcelBtn(): Locator {
    return this.page.locator('#generateExcelBtn');
  }

  async fillFilters(filters?: ReportFilters): Promise<void> {
    if (filters) {
      if (filters.tagNames) {
        for (let i = 0; i < filters.tagNames.length; i++) {
          await selectValueInNgSelector(this.page, '#tagSelector', filters.tagNames[i]);
        }
      }
      if (filters.dateRange) {
        await this.dateFormInput().click();
        await selectDateRangeOnNewDatePicker(
          this.page,
          filters.dateRange.yearFrom,
          filters.dateRange.monthFrom,
          filters.dateRange.dayFrom,
          filters.dateRange.yearTo,
          filters.dateRange.monthTo,
          filters.dateRange.dayTo
        );
      }
    }
  }
}

export class ReportsRowObject {
  constructor(
    private page: Page,
    private rowNum?: number,
    private rowName?: string
  ) {}

  getRowLocator(): Locator {
    if (this.rowName) {
      return this.page
        .locator('.mat-row')
        .filter({ hasText: this.rowName })
        .first();
    }
    return this.page.locator('.mat-row').nth((this.rowNum ?? 1) - 1);
  }

  static byIndex(page: Page, rowNum: number): ReportsRowObject {
    return new ReportsRowObject(page, rowNum);
  }

  static byName(page: Page, name: string): ReportsRowObject {
    return new ReportsRowObject(page, undefined, name);
  }
}

export class ReportFilters {
  tagNames?: string[] = [];
  dateRange?: {
    yearFrom: number;
    monthFrom: number;
    dayFrom: number;
    yearTo: number;
    monthTo: number;
    dayTo: number;
  };
}
