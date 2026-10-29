import {describe, it, expect, beforeEach, vi} from 'vitest';
import type {Mock} from 'vitest';
import {TestBed} from '@angular/core/testing';
import {MAT_DIALOG_DATA, MatDialogRef} from '@angular/material/dialog';
import {Subject, of, throwError} from 'rxjs';
import {BackendConfigurationPnTailBiteService} from '../../../../services';
import {TailBiteManagersDialogComponent} from './tail-bite-managers-dialog.component';

describe('TailBiteManagersDialogComponent', () => {
  let service: Record<string, Mock>;
  let component: TailBiteManagersDialogComponent;

  const create = (propertyId: number | null) => {
    TestBed.configureTestingModule({
      providers: [
        {provide: BackendConfigurationPnTailBiteService, useValue: service},
        {provide: MatDialogRef, useValue: {close: vi.fn()}},
        {provide: MAT_DIALOG_DATA, useValue: {propertyId}},
      ],
    });
    component = TestBed.runInInjectionContext(() => new TailBiteManagersDialogComponent());
    component.ngOnInit();
  };

  beforeEach(() => {
    service = {
      getProperties: vi.fn().mockImplementation(() => of({success: true, model: [
        {propertyId: 1, name: 'Ejendom Nord', enabled: true},
        {propertyId: 2, name: 'Ejendom Syd', enabled: false},
      ]})),
      getWorkers: vi.fn().mockReturnValue(of({success: true, model: [
        {siteId: 7, name: 'Jane Doe', isManager: false, propertyWorkerIds: [31, 32]},
      ]})),
      setManager: vi.fn().mockReturnValue(of({success: true})),
    };
  });

  it('preselects the filtered property and loads its workers', () => {
    create(2);
    expect(component.propertyId).toBe(2);
    expect(service.getWorkers).toHaveBeenCalledWith(2);
  });

  it('falls back to the first property', () => {
    create(null);
    expect(component.propertyId).toBe(1);
  });

  it('sets the flag on every PropertyWorker row of the worker and reloads', () => {
    create(1);
    service.getWorkers.mockClear();
    component.setManager(component.workers[0], true);
    expect(service.setManager.mock.calls).toEqual([[31, true], [32, true]]);
    expect(service.getWorkers).toHaveBeenCalledWith(1);
  });

  it('reloads the workers and frees the dialog when a toggle is refused', () => {
    create(1);
    service.getWorkers.mockClear();
    service.setManager.mockReturnValue(throwError(() => new Error('refused')));
    component.setManager(component.workers[0], true);
    expect(service.getWorkers).toHaveBeenCalledWith(1);
    expect(component.busy).toBe(false);
  });

  it('does not toggle the previous property\'s workers while the next list is pending', () => {
    create(1);
    const pending = new Subject<unknown>();
    service.getWorkers.mockReturnValueOnce(pending);
    component.onPropertyChange(2);
    expect(component.workers).toEqual([]);
    expect(service.setManager).not.toHaveBeenCalled();
    pending.next({success: true, model: [{siteId: 8, name: 'John Doe', isManager: false, propertyWorkerIds: [41]}]});
    expect(component.workers.map((w) => w.siteId)).toEqual([8]);
  });

  it('drops a late workers answer for a property that is no longer selected', () => {
    create(1);
    const late = new Subject<unknown>();
    service.getWorkers.mockReturnValueOnce(late);
    component.onPropertyChange(2);
    component.onPropertyChange(1);
    late.next({success: true, model: [{siteId: 8, name: 'John Doe', isManager: false, propertyWorkerIds: [41]}]});
    expect(component.workers.map((w) => w.siteId)).toEqual([7]);
  });

  it('drops a late answer for the same property asked for again in between (1 → 2 → 1)', () => {
    create(2);
    const late = new Subject<unknown>();
    service.getWorkers
      .mockReturnValueOnce(late)
      .mockReturnValueOnce(of({success: true, model: []}))
      .mockReturnValueOnce(of({success: true, model: [{siteId: 9, name: 'Jane Roe', isManager: true, propertyWorkerIds: [51]}]}));
    component.onPropertyChange(1);
    component.onPropertyChange(2);
    component.onPropertyChange(1);
    expect(late.observed).toBe(false);
    late.next({success: true, model: [{siteId: 8, name: 'John Doe', isManager: false, propertyWorkerIds: [41]}]});
    expect(component.workers.map((w) => w.siteId)).toEqual([9]);
  });

  it('shows no workers when the list fails to load', () => {
    create(1);
    service.getWorkers.mockReturnValueOnce(throwError(() => new Error('500')));
    component.onPropertyChange(2);
    expect(component.workers).toEqual([]);
  });

  it('survives a failing property list', () => {
    service.getProperties.mockReturnValueOnce(throwError(() => new Error('500')));
    create(1);
    expect(component.properties).toEqual([]);
    expect(component.propertyId).toBeNull();
  });

});
