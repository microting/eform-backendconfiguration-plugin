import {of} from 'rxjs';
import {TestBed} from '@angular/core/testing';
import {Router, ActivatedRoute} from '@angular/router';
import {Store} from '@ngrx/store';
import {MatDialog} from '@angular/material/dialog';
import {Overlay} from '@angular/cdk/overlay';
import {AppMenuStateService} from 'src/app/common/store';
import {BackendConfigurationPnAdhocService} from '../../../../services';
import {AdhocStateService} from '../store';
import {AdhocContainerComponent} from './adhoc-container.component';

/**
 * Spy-provider unit test for `AdhocContainerComponent`. Authored, not run -
 * jest runs from the host frontend only (repo convention).
 *
 * Uses TestBed.runInInjectionContext rather than the `new Component(...)`
 * idiom of adhoc-history.component.spec.ts, because this component uses
 * inject() field injection and has no constructor.
 */
describe('AdhocContainerComponent', () => {
  let adhocServiceSpy: any;
  let dialogSpy: any;
  let adhocStateServiceSpy: any;
  let component: AdhocContainerComponent;

  function buildComponent(): AdhocContainerComponent {
    adhocServiceSpy = {getTask: jest.fn()};
    dialogSpy = {open: jest.fn().mockReturnValue({afterClosed: () => of(false)})};
    adhocStateServiceSpy = {
      getTasks: jest.fn().mockReturnValue(of({success: true, model: {entities: [], openCount: 0, completedCount: 0, archivedCount: 0}})),
      loadProperties: jest.fn().mockReturnValue(of([])),
      loadTags: jest.fn().mockReturnValue(of([])),
      resetReferenceData: jest.fn(),
      currentFilters: {status: 'open'},
      currentPagination: {},
    };

    TestBed.configureTestingModule({
      providers: [
        {provide: Router, useValue: {url: '/adhoc'}},
        {provide: ActivatedRoute, useValue: {snapshot: {firstChild: null}}},
        {provide: AppMenuStateService, useValue: {leftAppMenus$: of([]), getTitleByUrl: jest.fn().mockReturnValue('Adhoc')}},
        {provide: Store, useValue: {select: jest.fn().mockReturnValue(of({})), dispatch: jest.fn()}},
        {provide: MatDialog, useValue: dialogSpy},
        // dialogConfigHelper reads overlay.scrollStrategies.reposition().
        {provide: Overlay, useValue: {scrollStrategies: {reposition: jest.fn().mockReturnValue({})}}},
        {provide: AdhocStateService, useValue: adhocStateServiceSpy},
        {provide: BackendConfigurationPnAdhocService, useValue: adhocServiceSpy},
      ],
    });

    return TestBed.runInInjectionContext(() => new AdhocContainerComponent());
  }

  beforeEach(() => {
    component = buildComponent();
  });

  it('onViewTask refetches the task by id and opens the drawer with the fresh model', () => {
    const fresh = {id: 42, title: 'Fix roof', photos: [{id: 9, contentType: 'image/jpeg'}]};
    adhocServiceSpy.getTask.mockReturnValue(of({success: true, model: fresh}));

    component.onViewTask({id: 42, title: 'stale', photos: []} as any);

    expect(adhocServiceSpy.getTask).toHaveBeenCalledWith(42);
    const openArgs = dialogSpy.open.mock.lastCall;
    expect(openArgs[1].data.mode).toBe('view');
    expect(openArgs[1].data.task).toBe(fresh);
  });

  it('onEditTask refetches the task by id and opens the drawer in edit mode', () => {
    const fresh = {id: 42, title: 'Fix roof', photos: []};
    adhocServiceSpy.getTask.mockReturnValue(of({success: true, model: fresh}));

    component.onEditTask({id: 42, title: 'stale', photos: []} as any);

    expect(adhocServiceSpy.getTask).toHaveBeenCalledWith(42);
    const openArgs = dialogSpy.open.mock.lastCall;
    expect(openArgs[1].data.mode).toBe('edit');
    expect(openArgs[1].data.task).toBe(fresh);
  });

  it('does not open the drawer when the refetch fails', () => {
    adhocServiceSpy.getTask.mockReturnValue(of({success: false, model: null}));

    component.onViewTask({id: 42} as any);

    expect(dialogSpy.open).not.toHaveBeenCalled();
  });

  // The stale photoIds the drawer would otherwise submit are what
  // ReconcilePhotosAsync soft-deletes by omission, so the copy flow must keep
  // using the copy endpoint's own fresh response rather than refetching.
  it('onCopyTask still opens the drawer with the copy modal result, without refetching', () => {
    const copied = {id: 99, title: 'Fix roof (copy)'};
    dialogSpy.open
      .mockReturnValueOnce({afterClosed: () => of(copied)})
      .mockReturnValueOnce({afterClosed: () => of(true)});

    component.onCopyTask({id: 42, title: 'Fix roof'} as any);

    expect(adhocServiceSpy.getTask).not.toHaveBeenCalled();
    const drawerArgs = dialogSpy.open.mock.calls[1];
    expect(drawerArgs[1].data.mode).toBe('edit');
    expect(drawerArgs[1].data.task).toBe(copied);
  });
});
