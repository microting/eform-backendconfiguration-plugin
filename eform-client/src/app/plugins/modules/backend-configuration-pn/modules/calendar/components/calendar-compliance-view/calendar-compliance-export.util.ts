import {TranslateService} from '@ngx-translate/core';
import {CalendarComplianceReportRowModel} from '../../../../models';

function formatTime(row: CalendarComplianceReportRowModel): string {
  if (row.isAllDay) { return ''; }
  const toHM = (h: number) => {
    const hh = Math.floor(h);
    const mm = Math.round((h - hh) * 60);
    return `${hh.toString().padStart(2, '0')}:${mm.toString().padStart(2, '0')}`;
  };
  return `${toHM(row.startHour)} - ${toHM(row.startHour + row.duration)}`;
}

function exportCells(row: CalendarComplianceReportRowModel, translate: TranslateService): string[] {
  return [
    row.taskDate,
    row.boardName,
    formatTime(row),
    row.title,
    row.propertyName,
    row.workerNames.join(', '),
    row.tags.join(', '),
    translate.instant(row.completed ? 'Completed tasks' : 'Not completed tasks'),
  ];
}

function exportHeaders(translate: TranslateService): string[] {
  return ['Date', 'Calendar', 'Time of day', 'Task', 'Property', 'Employees', 'Tags', 'Status']
    .map(k => translate.instant(k));
}

export function buildComplianceCsv(
  rows: CalendarComplianceReportRowModel[], translate: TranslateService
): string {
  const esc = (v: string) => `"${(v ?? '').replace(/"/g, '""')}"`;
  const lines = [exportHeaders(translate).map(esc).join(';')];
  for (const row of rows) {
    lines.push(exportCells(row, translate).map(esc).join(';'));
  }
  // UTF-8 BOM so Excel opens Danish characters correctly.
  return '﻿' + lines.join('\r\n');
}

export function buildComplianceExcelHtml(
  rows: CalendarComplianceReportRowModel[], translate: TranslateService
): string {
  const esc = (v: string) => (v ?? '')
    .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
  const th = exportHeaders(translate).map(h => `<th>${esc(h)}</th>`).join('');
  const trs = rows
    .map(r => `<tr>${exportCells(r, translate).map(c => `<td>${esc(c)}</td>`).join('')}</tr>`)
    .join('');
  return '﻿<html><head><meta charset="UTF-8"></head><body>' +
    `<table border="1"><thead><tr>${th}</tr></thead><tbody>${trs}</tbody></table></body></html>`;
}

export function downloadBlob(content: string, filename: string, mimeType: string): void {
  const blob = new Blob([content], {type: mimeType});
  const link = document.createElement('a');
  link.href = URL.createObjectURL(blob);
  link.download = filename;
  link.click();
  URL.revokeObjectURL(link.href);
}

export function openCompliancePdfWindow(
  rows: CalendarComplianceReportRowModel[], translate: TranslateService
): void {
  const esc = (v: string) => (v ?? '')
    .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
  const th = exportHeaders(translate).map(h => `<th>${esc(h)}</th>`).join('');
  const trs = rows
    .map(r => `<tr>${exportCells(r, translate).map(c => `<td>${esc(c)}</td>`).join('')}</tr>`)
    .join('');
  const w = window.open('', '_blank');
  if (!w) { return; }
  w.document.write(
    '<html><head><title>Compliance</title><style>' +
    'body{font-family:Roboto,Arial,sans-serif;font-size:12px}' +
    'table{border-collapse:collapse;width:100%}' +
    'th,td{border:1px solid #ccc;padding:4px 8px;text-align:left}' +
    'th{background:#f5f5f5}' +
    `</style></head><body><table><thead><tr>${th}</tr></thead><tbody>${trs}</tbody></table></body></html>`);
  w.document.close();
  w.focus();
  w.print();
}
