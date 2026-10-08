import {TestBed} from '@angular/core/testing';
import {MAT_DIALOG_DATA, MatDialogRef} from '@angular/material/dialog';
import {of, throwError} from 'rxjs';
import {BackendConfigurationPnTailBiteService} from '../../../../services';
import {TailBiteManagersDialogComponent} from './tail-bite-managers-dialog.component';

describe('TailBiteManagersDialogComponent', () => {
  let service: Record<string, jest.Mock>;
  let component: TailBiteManagersDialogComponent;
  let enabled: boolean;

  const create = (propertyId: number | null) => {
    TestBed.configureTestingModule({
      providers: [
        {provide: BackendConfigurationPnTailBiteService, useValue: service},
        {provide: MatDialogRef, useValue: {close: jest.fn()}},
        {provide: MAT_DIALOG_DATA, useValue: {propertyId}},
      ],
    });
    component = TestBed.runInInjectionContext(() => new TailBiteManagersDialogComponent());
    component.ngOnInit();
  };

  beforeEach(() => {
    enabled = false;
    service = {
      getProperties: jest.fn().mockImplementation(() => of({success: true, model: [
        {propertyId: 1, name: 'Ejendom Nord', enabled: true},
        {propertyId: 2, name: 'Ejendom Syd', enabled},
      ]})),
      getWorkers: jest.fn().mockReturnValue(of({success: true, model: [
        {siteId: 7, name: 'Jane Doe', isManager: false, propertyWorkerIds: [31, 32]},
      ]})),
      enable: jest.fn().mockImplementation(() => {
        enabled = true;
        return of({success: true});
      }),
      setManager: jest.fn().mockReturnValue(of({success: true})),
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

  it('enables tail biting and shows the property as enabled', () => {
    create(2);
    expect(component.selectedProperty!.enabled).toBe(false);
    component.enable();
    expect(service.enable).toHaveBeenCalledWith(2);
    expect(component.selectedProperty!.enabled).toBe(true);
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
});
