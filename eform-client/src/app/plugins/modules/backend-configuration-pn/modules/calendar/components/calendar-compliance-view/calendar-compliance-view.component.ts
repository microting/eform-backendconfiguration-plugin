import {Component, EventEmitter, Input, OnInit, Output, TemplateRef, ViewChild} from '@angular/core';
import {MatDialog, MatDialogRef} from '@angular/material/dialog';
import {TranslateService} from '@ngx-translate/core';
import {CommonDictionaryModel, SharedTagModel} from 'src/app/common/models';
import {
  CalendarBoardModel,
  CalendarComplianceReportRequestModel,
  CalendarComplianceReportRowModel,
} from '../../../../models';
import {BackendConfigurationPnCalendarService} from '../../../../services';
import {BackendConfigurationPnCompliancesService} from '../../../../services';
import {getCurrentLocale} from '../../services/calendar-locale.helper';
import {
  buildComplianceCsv,
  buildComplianceExcelHtml,
  downloadBlob,
  openCompliancePdfWindow,
} from './calendar-compliance-export.util';

interface ComplianceWeekGroup {
  label: string; // e.g. "Juli 2026 Uge 28"
  rows: CalendarComplianceReportRowModel[];
}

type PeriodPreset = '1' | '3' | '6' | '12' | 'ytd' | 'custom';

const PAGE_SIZE = 10;

@Component({
  standalone: false,
  selector: 'app-calendar-compliance-view',
  templateUrl: './calendar-compliance-view.component.html',
  styleUrls: ['./calendar-compliance-view.component.scss'],
})
export class CalendarComplianceViewComponent implements OnInit {
  @Input() properties: CommonDictionaryModel[] = [];
  @Input() boards: CalendarBoardModel[] = [];
  @Input() tags: SharedTagModel[] = [];
  @Input() employees: CommonDictionaryModel[] = [];
  @Input() currentPropertyId: number | null = null;

  @Output() completeRequested = new EventEmitter<CalendarComplianceReportRowModel>();

  @ViewChild('deleteConfirmTpl') deleteConfirmTpl!: TemplateRef<unknown>;

  // Filters (null = all).
  filterPropertyId: number | null = null;
  filterBoardId: number | null = null;
  filterTagId: number | null = null;
  filterStatus: 'open' | 'done' | 'all' = 'open';
  filterSiteId: number | null = null;
  periodPreset: PeriodPreset = '1';
  customFrom: Date | null = null;
  customTo: Date | null = null;

  exportFormat: '' | 'pdf' | 'csv' | 'excel' = '';

  loading = false;
  hasFetched = false;
  rows: CalendarComplianceReportRowModel[] = [];
  groups: ComplianceWeekGroup[] = [];
  visibleGroups: ComplianceWeekGroup[] = [];
  pageIndex = 0;
  showAll = false;

  statusOptions: {value: 'open' | 'done' | 'all'; label: string}[] = [];
  periodOptions: {value: PeriodPreset; label: string}[] = [];
  exportOptions: {value: 'pdf' | 'csv' | 'excel'; label: string}[] = [];

  private deleteDialogRef: MatDialogRef<unknown> | null = null;
  private pendingDeleteId: number | null = null;

  constructor(
    private calendarService: BackendConfigurationPnCalendarService,
    private compliancesService: BackendConfigurationPnCompliancesService,
    private translate: TranslateService,
    private dialog: MatDialog,
  ) {}

  ngOnInit(): void {
    this.filterPropertyId = this.currentPropertyId;

    this.statusOptions = [
      {value: 'open', label: this.translate.instant('Not completed tasks')},
      {value: 'done', label: this.translate.instant('Completed tasks')},
      {value: 'all', label: this.translate.instant('All tasks')},
    ];
    this.periodOptions = [
      {value: '1', label: this.translate.instant('1 month')},
      {value: '3', label: this.translate.instant('3 months')},
      {value: '6', label: this.translate.instant('6 months')},
      {value: '12', label: this.translate.instant('12 months')},
      {value: 'ytd', label: this.translate.instant('Year to date')},
      {value: 'custom', label: this.translate.instant('Set period')},
    ];
    this.exportOptions = [
      {value: 'pdf', label: 'PDF'},
      {value: 'csv', label: 'CSV'},
      {value: 'excel', label: 'Excel'},
    ];
  }

  get dateFrom(): Date {
    const today = new Date();
    switch (this.periodPreset) {
      case 'ytd': return new Date(today.getFullYear(), 0, 1);
      case 'custom': return this.customFrom ?? today;
      default: {
        const months = parseInt(this.periodPreset, 10);
        return this.addClampedMonths(today, -months);
      }
    }
  }

  get dateTo(): Date {
    const today = new Date();
    switch (this.periodPreset) {
      case 'custom': return this.customTo ?? today;
      case 'ytd':
        // Open/all tasks can have future deadlines; only "done" stays
        // retrospective (bounded by today).
        return this.filterStatus === 'done' ? today : new Date(today.getFullYear(), 11, 31);
      default: {
        if (this.filterStatus === 'done') { return today; }
        const months = parseInt(this.periodPreset, 10);
        return this.addClampedMonths(today, months);
      }
    }
  }

  // setMonth overflows at month ends (May 31 + 3 → Sep 1); clamp back to the
  // last day of the intended month. `months` may be negative.
  private addClampedMonths(date: Date, months: number): Date {
    const d = new Date(date);
    const targetMonthIndex = d.getMonth() + months;
    d.setMonth(targetMonthIndex);
    if (d.getMonth() !== ((targetMonthIndex % 12) + 12) % 12) {
      d.setDate(0);
    }
    return d;
  }

  get periodDisplay(): string {
    const locale = getCurrentLocale(this.translate);
    const fmt = (d: Date) => d.toLocaleDateString(locale, {day: 'numeric', month: 'long', year: 'numeric'});
    return `${fmt(this.dateFrom)} – ${fmt(this.dateTo)}`;
  }

  get canShowReport(): boolean {
    if (this.periodPreset !== 'custom') { return true; }
    return !!this.customFrom && !!this.customTo && this.customFrom <= this.customTo;
  }

  get canDownload(): boolean {
    return !!this.exportFormat && this.hasFetched && this.rows.length > 0;
  }

  get flatRows(): CalendarComplianceReportRowModel[] { return this.rows; }
  get totalRows(): number { return this.rows.length; }
  get totalPages(): number { return Math.max(1, Math.ceil(this.totalRows / PAGE_SIZE)); }
  get pageNumbers(): (number | 'gap')[] {
    const total = this.totalPages;
    if (total <= 9) { return Array.from({length: total}, (_, i) => i); }
    const around = [this.pageIndex - 2, this.pageIndex - 1, this.pageIndex, this.pageIndex + 1, this.pageIndex + 2]
      .filter(i => i > 0 && i < total - 1);
    const result: (number | 'gap')[] = [0];
    if (around.length === 0 || around[0] > 1) { result.push('gap'); }
    result.push(...around);
    if (around.length === 0 || around[around.length - 1] < total - 2) { result.push('gap'); }
    result.push(total - 1);
    return result;
  }
  get showingFrom(): number {
    return this.totalRows === 0 ? 0 : (this.showAll ? 1 : this.pageIndex * PAGE_SIZE + 1);
  }
  get showingTo(): number {
    return this.showAll ? this.totalRows : Math.min((this.pageIndex + 1) * PAGE_SIZE, this.totalRows);
  }

  showReport(): void {
    if (!this.canShowReport) { return; }
    const toIso = (d: Date) =>
      `${d.getFullYear()}-${(d.getMonth() + 1).toString().padStart(2, '0')}-${d.getDate().toString().padStart(2, '0')}`;
    const model: CalendarComplianceReportRequestModel = {
      propertyId: this.filterPropertyId,
      boardIds: this.filterBoardId ? [this.filterBoardId] : [],
      tagIds: this.filterTagId ? [this.filterTagId] : [],
      siteIds: this.filterSiteId ? [this.filterSiteId] : [],
      status: this.filterStatus,
      dateFrom: toIso(this.dateFrom),
      dateTo: toIso(this.dateTo),
    };
    this.loading = true;
    this.calendarService.getComplianceReport(model).subscribe({
      next: res => {
        this.loading = false;
        if (!res?.success) { return; }
        this.hasFetched = true;
        this.rows = res.model ?? [];
        this.pageIndex = 0;
        this.showAll = false;
        this.groups = this.groupRows(this.rows);
        this.updateVisibleGroups();
      },
      error: () => { this.loading = false; },
    });
  }

  refresh(): void {
    if (this.hasFetched) { this.showReport(); }
  }

  onPropertyFilterChanged(value: number | null): void {
    this.filterPropertyId = value;
    this.filterBoardId = null;
    this.filterSiteId = null;
  }

  onRowClicked(row: CalendarComplianceReportRowModel): void {
    if (!this.isRowCompletable(row)) { return; }
    this.completeRequested.emit(row);
  }

  isRowCompletable(row: CalendarComplianceReportRowModel): boolean {
    return !row.completed && row.areaRulePlanningId != null;
  }

  // --- delete ---
  openDeleteConfirm(row: CalendarComplianceReportRowModel, event: MouseEvent): void {
    event.stopPropagation();
    this.pendingDeleteId = row.complianceId;
    this.deleteDialogRef = this.dialog.open(this.deleteConfirmTpl, {autoFocus: false});
  }

  cancelDelete(): void {
    this.deleteDialogRef?.close();
    this.pendingDeleteId = null;
  }

  confirmDelete(): void {
    const id = this.pendingDeleteId;
    this.deleteDialogRef?.close();
    this.pendingDeleteId = null;
    if (!id) { return; }
    this.compliancesService.deleteCompliance(id).subscribe(res => {
      if (res?.success) { this.showReport(); }
    });
  }

  // --- pagination ---
  goToPage(i: number): void { this.pageIndex = i; this.showAll = false; this.updateVisibleGroups(); }
  prevPage(): void { if (this.pageIndex > 0) { this.pageIndex--; this.updateVisibleGroups(); } }
  nextPage(): void { if (this.pageIndex < this.totalPages - 1) { this.pageIndex++; this.updateVisibleGroups(); } }
  toggleShowAll(): void { this.showAll = true; this.updateVisibleGroups(); }

  trackByGroup(_: number, group: ComplianceWeekGroup): string { return group.label; }
  trackByRow(_: number, row: CalendarComplianceReportRowModel): number { return row.complianceId; }

  // --- export ---
  download(): void {
    if (!this.canDownload) { return; }
    const stamp = new Date().toISOString().slice(0, 10);
    switch (this.exportFormat) {
      case 'csv':
        downloadBlob(buildComplianceCsv(this.rows, this.translate),
          `compliance-${stamp}.csv`, 'text/csv;charset=utf-8');
        break;
      case 'excel':
        downloadBlob(buildComplianceExcelHtml(this.rows, this.translate),
          `compliance-${stamp}.xls`, 'application/vnd.ms-excel');
        break;
      case 'pdf':
        openCompliancePdfWindow(this.rows, this.translate);
        break;
    }
  }

  // --- display helpers ---
  formatDayLabel(taskDate: string): string {
    const d = new Date(taskDate);
    const s = d.toLocaleDateString(getCurrentLocale(this.translate),
      {weekday: 'long', day: 'numeric', month: 'long'});
    return s.charAt(0).toUpperCase() + s.slice(1);
  }

  formatTimeRange(row: CalendarComplianceReportRowModel): string {
    if (row.isAllDay) { return ''; }
    const toHM = (h: number) => {
      const hh = Math.floor(h);
      const mm = Math.round((h - hh) * 60);
      return `${hh.toString().padStart(2, '0')}:${mm.toString().padStart(2, '0')}`;
    };
    return `${toHM(row.startHour)} - ${toHM(row.startHour + row.duration)}`;
  }

  private updateVisibleGroups(): void {
    if (this.showAll) {
      this.visibleGroups = this.groups;
      return;
    }
    const start = this.pageIndex * PAGE_SIZE;
    const pageRows = this.flatRows.slice(start, start + PAGE_SIZE);
    this.visibleGroups = this.groupRows(pageRows);
  }

  private groupRows(rows: CalendarComplianceReportRowModel[]): ComplianceWeekGroup[] {
    const locale = getCurrentLocale(this.translate);
    const groups: ComplianceWeekGroup[] = [];
    const byKey = new Map<string, ComplianceWeekGroup>();
    for (const row of rows) {
      const d = new Date(row.taskDate);
      const iso = this.isoWeek(d);
      const month = d.toLocaleDateString(locale, {month: 'long', year: 'numeric'});
      const monthLabel = month.charAt(0).toUpperCase() + month.slice(1);
      const key = `${iso.year}-W${iso.week}`;
      let group = byKey.get(key);
      if (!group) {
        group = {label: `${monthLabel} ${this.translate.instant('Week')} ${iso.week}`, rows: []};
        byKey.set(key, group);
        groups.push(group);
      }
      group.rows.push(row);
    }
    return groups;
  }

  private isoWeek(date: Date): {week: number; year: number} {
    const d = new Date(Date.UTC(date.getFullYear(), date.getMonth(), date.getDate()));
    const dayNum = d.getUTCDay() || 7;
    d.setUTCDate(d.getUTCDate() + 4 - dayNum);
    const year = d.getUTCFullYear();
    const yearStart = new Date(Date.UTC(year, 0, 1));
    const week = Math.ceil((((d.getTime() - yearStart.getTime()) / 86400000) + 1) / 7);
    return {week, year};
  }
}
