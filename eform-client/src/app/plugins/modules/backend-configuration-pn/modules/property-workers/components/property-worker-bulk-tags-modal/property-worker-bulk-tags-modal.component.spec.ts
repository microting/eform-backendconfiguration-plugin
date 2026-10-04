import {of, Subject} from 'rxjs';
import {TestBed} from '@angular/core/testing';
import {MAT_DIALOG_DATA, MatDialogRef} from '@angular/material/dialog';
import {BackendConfigurationPnPropertiesService} from '../../../../services';
import {WorkerTagsBulkMode} from '../../../../models';
import {PropertyWorkerBulkTagsModalComponent, PropertyWorkerBulkTagsModalData} from './property-worker-bulk-tags-modal.component';

/**
 * #1380 — the "Tildel tags" dialog. Authored, not run locally - jest runs from
 * the host frontend in CI. Built with TestBed.runInInjectionContext because the
 * component uses inject() field injection.
 */
describe('PropertyWorkerBulkTagsModalComponent', () => {
  let serviceSpy: {bulkUpdateWorkerTags: jest.Mock};
  let dialogRefSpy: {close: jest.Mock};
  let component: PropertyWorkerBulkTagsModalComponent;

  const data: PropertyWorkerBulkTagsModalData = {
    workers: [{siteId: 11, siteName: 'Jane Doe'} as any, {siteId: 12, siteName: 'John Doe'} as any],
    availableTags: [{id: 5, name: 'Team A'} as any],
  };

  beforeEach(() => {
    serviceSpy = {bulkUpdateWorkerTags: jest.fn().mockReturnValue(of({success: true}))};
    dialogRefSpy = {close: jest.fn()};
    TestBed.configureTestingModule({
      providers: [
        {provide: BackendConfigurationPnPropertiesService, useValue: serviceSpy},
        {provide: MatDialogRef, useValue: dialogRefSpy},
        {provide: MAT_DIALOG_DATA, useValue: data},
      ],
    });
    component = TestBed.runInInjectionContext(() => new PropertyWorkerBulkTagsModalComponent());
  });

  it('defaults to Add and is not savable without a tag', () => {
    expect(component.mode).toBe(WorkerTagsBulkMode.Add);
    expect(component.valid).toBe(false);

    component.save();

    expect(serviceSpy.bulkUpdateWorkerTags).not.toHaveBeenCalled();
  });

  it('sends every selected worker, the chosen tags and the mode, then closes with true', () => {
    component.tagIds = [5];
    component.mode = WorkerTagsBulkMode.Remove;

    component.save();

    expect(serviceSpy.bulkUpdateWorkerTags).toHaveBeenCalledWith({
      siteIds: [11, 12],
      tagIds: [5],
      mode: WorkerTagsBulkMode.Remove,
    });
    expect(dialogRefSpy.close).toHaveBeenCalledWith(true);
  });

  it('stays open when the backend refuses', () => {
    serviceSpy.bulkUpdateWorkerTags.mockReturnValue(of({success: false, message: 'refused'}));
    component.tagIds = [5];

    component.save();

    expect(dialogRefSpy.close).not.toHaveBeenCalled();
    expect(component.saving).toBe(false);
  });

  it('ignores a second save while the first is in flight', () => {
    const response$ = new Subject<any>();
    serviceSpy.bulkUpdateWorkerTags.mockReturnValue(response$);
    component.tagIds = [5];

    component.save();
    component.save();

    expect(serviceSpy.bulkUpdateWorkerTags).toHaveBeenCalledTimes(1);
    response$.next({success: true});
    expect(dialogRefSpy.close).toHaveBeenCalledWith(true);
  });
});
