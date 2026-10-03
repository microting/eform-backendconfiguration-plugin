import {ElementRef} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {Overlay} from '@angular/cdk/overlay';
import {MatDialog} from '@angular/material/dialog';
import {ActivatedRoute, Router} from '@angular/router';
import {Store} from '@ngrx/store';
import {TranslateService} from '@ngx-translate/core';
import {of} from 'rxjs';
import {selectCurrentUserIsAdmin} from 'src/app/state';
import {TaskModel} from '../../../../models';
import {TaskTrackerStateService} from '../store';
import {TaskTrackerTableComponent} from './task-tracker-table.component';

/**
 * #1300 — the task tracker's "Delete Case" menu item is withheld from an
 * uncompleted task dated after today (Copenhagen date). Every task-tracker row
 * is an uncompleted occurrence. The gate reads `complianceDeadline`, the
 * compliance's own stored date that the server's guard uses (#1382); the
 * displayed `deadlineTask` is a day earlier for legacy rows and plays no part.
 *
 * Built with `new` inside an injection context (the component uses `inject()`),
 * so no template or mtx-grid is involved — this pins the gate, not the DOM.
 * Clock: 2026-09-18 12:00 in Copenhagen.
 */
describe('TaskTrackerTableComponent — Delete Case gate (#1300)', () => {
  let component: TaskTrackerTableComponent;
  let dialogOpen: jest.Mock;
  let isAdmin: boolean;

  const task = (storedUtc: string, overrides: Partial<TaskModel> = {}): TaskModel =>
    ({
      complianceId: 1,
      createdInWizard: true,
      movedToExpiredFolder: false,
      complianceDeadline: new Date(storedUtc),
      deadlineTask: new Date(storedUtc),
      ...overrides,
    }) as unknown as TaskModel;

  beforeEach(() => {
    jest.useFakeTimers();
    jest.setSystemTime(new Date('2026-09-18T10:00:00Z'));
    dialogOpen = jest.fn(() => ({afterClosed: () => of(false)}));
    isAdmin = true;

    TestBed.configureTestingModule({
      providers: [
        {provide: MatDialog, useValue: {open: dialogOpen}},
        {provide: Overlay, useValue: {scrollStrategies: {reposition: jest.fn()}}},
        {provide: Store, useValue: {select: jest.fn((selector: unknown) => of(selector === selectCurrentUserIsAdmin ? isAdmin : false))}},
        {provide: TranslateService, useValue: {stream: jest.fn(() => of('')), instant: jest.fn((k: string) => k)}},
        {provide: TaskTrackerStateService, useValue: {}},
        {provide: ActivatedRoute, useValue: {}},
        {provide: Router, useValue: {navigate: jest.fn()}},
        {provide: ElementRef, useValue: new ElementRef(document.createElement('div'))},
      ],
    });
    component = TestBed.runInInjectionContext(() => new TaskTrackerTableComponent());
  });

  afterEach(() => {
    jest.useRealTimers();
  });

  it.each([
    ['stored yesterday', '2026-09-17T00:00:00Z', true],
    ['stored today', '2026-09-18T00:00:00Z', true],
    ['stored tomorrow', '2026-09-19T00:00:00Z', false],
    ['stored in a week', '2026-09-25T00:00:00Z', false],
  ])('%s → deletable: %s', (_label, stored, expected) => {
    expect(component.canDeleteTask(task(stored as string))).toBe(expected);
  });

  it('flips at Copenhagen midnight (CEST), not UTC midnight', () => {
    const storedTomorrow = task('2026-09-19T00:00:00Z');
    jest.setSystemTime(new Date('2026-09-18T21:59:00Z')); // 23:59 local
    expect(component.canDeleteTask(storedTomorrow)).toBe(false);
    jest.setSystemTime(new Date('2026-09-18T22:00:00Z')); // 00:00 local on the 19th
    expect(component.canDeleteTask(storedTomorrow)).toBe(true);
  });

  it('judges a legacy row by its stored date, not its display date a day earlier', () => {
    // Displayed today, stored tomorrow: the server would refuse, so no action.
    const legacy = task('2026-09-19T00:00:00Z', {deadlineTask: new Date('2026-09-18T00:00:00Z')});
    expect(component.canDeleteTask(legacy)).toBe(false);
  });

  it('keeps the existing wizard / expired-folder conditions', () => {
    expect(component.canDeleteTask(task('2026-09-10T00:00:00Z', {createdInWizard: false}))).toBe(false);
    expect(component.canDeleteTask(task('2026-09-10T00:00:00Z', {movedToExpiredFolder: true}))).toBe(false);
  });

  it('withholds Delete Case from a non-admin', () => {
    isAdmin = false;
    const nonAdmin = TestBed.runInInjectionContext(() => new TaskTrackerTableComponent());
    expect(nonAdmin.canDeleteTask(task('2026-09-10T00:00:00Z'))).toBe(false);
  });

  it('does not open the delete dialog for a future task even if invoked directly', () => {
    component.onShowDeleteComplianceModal(task('2026-09-19T00:00:00Z'));
    expect(dialogOpen).not.toHaveBeenCalled();
  });

  it('opens the delete dialog for today\'s task', () => {
    component.onShowDeleteComplianceModal(task('2026-09-18T00:00:00Z'));
    expect(dialogOpen).toHaveBeenCalledTimes(1);
  });
});
