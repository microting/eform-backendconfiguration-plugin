import {TestBed} from '@angular/core/testing';
import {Overlay} from '@angular/cdk/overlay';
import {MatDialog} from '@angular/material/dialog';
import {ActivatedRoute, Router} from '@angular/router';
import {Store} from '@ngrx/store';
import {TranslateService} from '@ngx-translate/core';
import {of} from 'rxjs';
import {AuthStateService} from 'src/app/common/store';
import {ComplianceModel} from '../../../../models';
import {CompliancesStateService} from '../store';
import {CompliancesTableComponent} from './compliances-table.component';

/**
 * #1300 — the legacy `/compliances` table. Every row is an uncompleted
 * occurrence; its `deadline` is the DISPLAYED deadline, `Compliance.Deadline −
 * 1 day`, so "displayed today" is a task dated TOMORROW.
 *
 * `canEdit` used to compare timestamps (`deadline < now`), which let a task
 * dated tomorrow be filled in from 00:00 UTC today. It is now date-level on the
 * Copenhagen date, and the admin-only delete button follows the same rule.
 * Clock: 2026-09-18 12:00 in Copenhagen.
 */
describe('CompliancesTableComponent — future tasks (#1300)', () => {
  let component: CompliancesTableComponent;
  let dialogOpen: jest.Mock;

  beforeEach(() => {
    jest.useFakeTimers();
    jest.setSystemTime(new Date('2026-09-18T10:00:00Z'));
    dialogOpen = jest.fn(() => ({afterClosed: () => of(false)}));

    TestBed.configureTestingModule({
      providers: [
        {provide: Store, useValue: {select: jest.fn(() => of(true))}},
        {provide: CompliancesStateService, useValue: {}},
        {provide: AuthStateService, useValue: {}},
        {provide: TranslateService, useValue: {stream: jest.fn(() => of(''))}},
        {provide: Router, useValue: {navigate: jest.fn()}},
        {provide: MatDialog, useValue: {open: dialogOpen}},
        {provide: Overlay, useValue: {scrollStrategies: {reposition: jest.fn()}}},
        {provide: ActivatedRoute, useValue: {}},
      ],
    });
    component = TestBed.runInInjectionContext(() => new CompliancesTableComponent());
  });

  afterEach(() => {
    jest.useRealTimers();
  });

  it.each([
    ['displayed two days ago → task yesterday', '2026-09-16T00:00:00Z', true],
    ['displayed yesterday → task today', '2026-09-17T00:00:00Z', true],
    ['displayed today → task tomorrow (the old timestamp check allowed this)', '2026-09-18T00:00:00Z', false],
    ['displayed in a week', '2026-09-25T00:00:00Z', false],
  ])('canEdit / canDelete: %s → %s', (_label, deadline, expected) => {
    const date = new Date(deadline as string);
    expect(component.canEdit(date)).toBe(expected);
    expect(component.canDelete(date)).toBe(expected);
  });

  it('flips at Copenhagen midnight (CEST), not UTC midnight', () => {
    const displayedToday = new Date('2026-09-18T00:00:00Z'); // task dated the 19th
    jest.setSystemTime(new Date('2026-09-18T21:59:00Z')); // 23:59 local
    expect(component.canEdit(displayedToday)).toBe(false);
    jest.setSystemTime(new Date('2026-09-18T22:00:00Z')); // 00:00 local on the 19th
    expect(component.canEdit(displayedToday)).toBe(true);
  });

  it('gates the admin delete button through the grid iif', () => {
    const actions = component.adminTableHeaders.find((h) => h.field === 'actions')!;
    const deleteBtn = (actions.buttons as any[]).find((b) => b.icon === 'delete');
    const row = (deadline: string) => ({deadline: new Date(deadline)}) as ComplianceModel;
    expect(deleteBtn.iif(row('2026-09-18T00:00:00Z'))).toBe(false);
    expect(deleteBtn.iif(row('2026-09-17T00:00:00Z'))).toBe(true);
  });

  it('does not open the delete dialog for a future row even if invoked directly', () => {
    component.onShowDeleteComplianceModal({deadline: new Date('2026-09-18T00:00:00Z')} as any);
    expect(dialogOpen).not.toHaveBeenCalled();
    component.onShowDeleteComplianceModal({deadline: new Date('2026-09-17T00:00:00Z')} as any);
    expect(dialogOpen).toHaveBeenCalledTimes(1);
  });
});
