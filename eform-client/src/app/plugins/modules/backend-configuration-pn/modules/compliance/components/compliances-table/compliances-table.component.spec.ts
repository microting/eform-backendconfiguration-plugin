import {TestBed} from '@angular/core/testing';
import {Overlay} from '@angular/cdk/overlay';
import {MatDialog} from '@angular/material/dialog';
import {ActivatedRoute, Router} from '@angular/router';
import {Store} from '@ngrx/store';
import {TranslateService} from '@ngx-translate/core';
import {of} from 'rxjs';
import {AuthStateService} from 'src/app/common/store';
import {selectAuthIsAdmin} from 'src/app/state/auth/auth.selector';
import {ComplianceModel} from '../../../../models';
import {CompliancesStateService} from '../store';
import {CompliancesTableComponent} from './compliances-table.component';

/**
 * #1300 — the legacy `/compliances` table. Every row is an uncompleted
 * occurrence. Edit and delete are judged on `complianceDeadline`, the
 * compliance's own stored date — the date the server's guard uses (#1382).
 * The displayed `deadline` is a day earlier for legacy rows and plays no part.
 *
 * The check is date-level on the Copenhagen date, and the admin-only delete
 * button follows the same rule. Clock: 2026-09-18 12:00 in Copenhagen.
 */
describe('CompliancesTableComponent — future tasks (#1300)', () => {
  let component: CompliancesTableComponent;
  let dialogOpen: jest.Mock;
  let isAdmin: boolean;

  beforeEach(() => {
    jest.useFakeTimers();
    jest.setSystemTime(new Date('2026-09-18T10:00:00Z'));
    dialogOpen = jest.fn(() => ({afterClosed: () => of(false)}));
    isAdmin = true;

    TestBed.configureTestingModule({
      providers: [
        // Signed in (every selector truthy) except where a case flips the admin role.
        {provide: Store, useValue: {select: jest.fn((selector: unknown) => of(selector === selectAuthIsAdmin ? isAdmin : true))}},
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
    ['stored yesterday', '2026-09-17T00:00:00Z', true],
    ['stored today', '2026-09-18T00:00:00Z', true],
    ['stored tomorrow', '2026-09-19T00:00:00Z', false],
    ['stored in a week', '2026-09-25T00:00:00Z', false],
  ])('canEdit / canDelete: %s → %s', (_label, stored, expected) => {
    const date = new Date(stored as string);
    expect(component.canEdit(date)).toBe(expected);
    expect(component.canDelete(date)).toBe(expected);
  });

  it('flips at Copenhagen midnight (CEST), not UTC midnight', () => {
    const storedTomorrow = new Date('2026-09-19T00:00:00Z');
    jest.setSystemTime(new Date('2026-09-18T21:59:00Z')); // 23:59 local
    expect(component.canEdit(storedTomorrow)).toBe(false);
    jest.setSystemTime(new Date('2026-09-18T22:00:00Z')); // 00:00 local on the 19th
    expect(component.canEdit(storedTomorrow)).toBe(true);
  });

  const row = (stored: string, displayed = stored) =>
    ({complianceDeadline: new Date(stored), deadline: new Date(displayed)}) as ComplianceModel;

  it('gates the admin edit and delete buttons through the grid iif on the stored date', () => {
    const actions = component.adminTableHeaders.find((h) => h.field === 'actions')!;
    const editBtn = (actions.buttons as any[]).find((b) => b.icon === 'edit');
    const deleteBtn = (actions.buttons as any[]).find((b) => b.icon === 'delete');
    expect(deleteBtn.iif(row('2026-09-19T00:00:00Z'))).toBe(false);
    expect(deleteBtn.iif(row('2026-09-18T00:00:00Z'))).toBe(true);
    expect(editBtn.iif(row('2026-09-19T00:00:00Z'))).toBe(false);
    expect(editBtn.iif(row('2026-09-18T00:00:00Z'))).toBe(true);
  });

  it('judges a legacy row by its stored date, not its display date a day earlier', () => {
    const actions = component.adminTableHeaders.find((h) => h.field === 'actions')!;
    const deleteBtn = (actions.buttons as any[]).find((b) => b.icon === 'delete');
    // Displayed today, stored tomorrow: the server would refuse, so no action.
    expect(deleteBtn.iif(row('2026-09-19T00:00:00Z', '2026-09-18T00:00:00Z'))).toBe(false);
  });

  it.each([
    [true, true],
    [false, false],
  ])('admin=%s → the delete action is in the grid: %s', (admin, hasDelete) => {
    // The component selects the flag at construction, so build it after setting the role.
    isAdmin = admin;
    const table = TestBed.runInInjectionContext(() => new CompliancesTableComponent());
    table.ngOnInit();
    const actions = table.mergedTableHeaders.find((h) => h.field === 'actions')!;
    expect((actions.buttons as any[]).some((b) => b.icon === 'delete')).toBe(hasDelete);
  });

  it('does not open the delete dialog for a future row even if invoked directly', () => {
    component.onShowDeleteComplianceModal(row('2026-09-19T00:00:00Z') as any);
    expect(dialogOpen).not.toHaveBeenCalled();
    component.onShowDeleteComplianceModal(row('2026-09-18T00:00:00Z') as any);
    expect(dialogOpen).toHaveBeenCalledTimes(1);
  });
});
