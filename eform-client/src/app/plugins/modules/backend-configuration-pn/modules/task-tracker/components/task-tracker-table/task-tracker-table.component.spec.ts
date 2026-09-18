import {ElementRef} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {Overlay} from '@angular/cdk/overlay';
import {MatDialog} from '@angular/material/dialog';
import {ActivatedRoute, Router} from '@angular/router';
import {Store} from '@ngrx/store';
import {TranslateService} from '@ngx-translate/core';
import {of} from 'rxjs';
import {TaskModel} from '../../../../models';
import {TaskTrackerStateService} from '../store';
import {TaskTrackerTableComponent} from './task-tracker-table.component';

/**
 * #1300 — the task tracker's "Delete Case" menu item is withheld from an
 * uncompleted task dated after today (Copenhagen date). Every task-tracker row
 * is an uncompleted occurrence. `deadlineTask` is the DISPLAYED deadline,
 * `Compliance.Deadline − 1 day`, so "displayed today" is a task dated TOMORROW.
 *
 * Built with `new` inside an injection context (the component uses `inject()`),
 * so no template or mtx-grid is involved — this pins the gate, not the DOM.
 * Clock: 2026-09-18 12:00 in Copenhagen.
 */
describe('TaskTrackerTableComponent — Delete Case gate (#1300)', () => {
  let component: TaskTrackerTableComponent;
  let dialogOpen: jest.Mock;

  const task = (deadlineTaskUtc: string, overrides: Partial<TaskModel> = {}): TaskModel =>
    ({
      complianceId: 1,
      createdInWizard: true,
      movedToExpiredFolder: false,
      deadlineTask: new Date(deadlineTaskUtc),
      ...overrides,
    }) as unknown as TaskModel;

  beforeEach(() => {
    jest.useFakeTimers();
    jest.setSystemTime(new Date('2026-09-18T10:00:00Z'));
    dialogOpen = jest.fn(() => ({afterClosed: () => of(false)}));

    TestBed.configureTestingModule({
      providers: [
        {provide: MatDialog, useValue: {open: dialogOpen}},
        {provide: Overlay, useValue: {scrollStrategies: {reposition: jest.fn()}}},
        {provide: Store, useValue: {select: jest.fn(() => of(false))}},
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
    ['displayed two days ago → task yesterday', '2026-09-16T00:00:00Z', true],
    ['displayed yesterday → task today', '2026-09-17T00:00:00Z', true],
    ['displayed today → task tomorrow', '2026-09-18T00:00:00Z', false],
    ['displayed in a week → future', '2026-09-25T00:00:00Z', false],
  ])('%s → deletable: %s', (_label, deadline, expected) => {
    expect(component.canDeleteTask(task(deadline as string))).toBe(expected);
  });

  it('keeps the existing wizard / expired-folder conditions', () => {
    expect(component.canDeleteTask(task('2026-09-10T00:00:00Z', {createdInWizard: false}))).toBe(false);
    expect(component.canDeleteTask(task('2026-09-10T00:00:00Z', {movedToExpiredFolder: true}))).toBe(false);
  });

  it('does not open the delete dialog for a future task even if invoked directly', () => {
    component.onShowDeleteComplianceModal(task('2026-09-18T00:00:00Z'));
    expect(dialogOpen).not.toHaveBeenCalled();
  });

  it('opens the delete dialog for today\'s task', () => {
    component.onShowDeleteComplianceModal(task('2026-09-17T00:00:00Z'));
    expect(dialogOpen).toHaveBeenCalledTimes(1);
  });
});
