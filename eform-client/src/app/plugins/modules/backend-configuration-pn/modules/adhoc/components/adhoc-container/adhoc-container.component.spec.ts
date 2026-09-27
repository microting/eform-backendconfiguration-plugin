import {of} from 'rxjs';
import {NO_ERRORS_SCHEMA} from '@angular/core';
import {ComponentFixture, TestBed} from '@angular/core/testing';
import {By} from '@angular/platform-browser';
import {MatButtonToggleGroup, MatButtonToggleModule} from '@angular/material/button-toggle';
import {TranslateModule} from '@ngx-translate/core';
import {Router, ActivatedRoute} from '@angular/router';
import {Store} from '@ngrx/store';
import {MatDialog} from '@angular/material/dialog';
import {Overlay} from '@angular/cdk/overlay';
import {AppMenuStateService} from 'src/app/common/store';
import {BackendConfigurationPnAdhocService} from '../../../../services';
import {AdhocStateService} from '../store';
import {AdhocContainerComponent, AdhocViewMode} from './adhoc-container.component';

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

/**
 * The Overblik/Historik switcher (#1331): a stock mat-button-toggle-group
 * whose value is derived from the route and whose (change) navigates.
 * Rendered through TestBed with only MatButtonToggleModule real; the
 * filters, table, pagination and router-outlet are unknown elements under
 * NO_ERRORS_SCHEMA.
 */
describe('AdhocContainerComponent — view switcher', () => {
  let fixture: ComponentFixture<AdhocContainerComponent>;
  let routerSpy: {url: string; navigate: jest.Mock};
  let routeStub: {snapshot: {firstChild: any}};

  const group = (): MatButtonToggleGroup =>
    fixture.debugElement.query(By.directive(MatButtonToggleGroup)).injector.get(MatButtonToggleGroup);

  const toggleHost = (view: AdhocViewMode): HTMLElement =>
    fixture.nativeElement.querySelector(`#backend-configuration-pn-adhoc-view-${view}`);

  const innerButton = (view: AdhocViewMode): HTMLButtonElement =>
    fixture.nativeElement.querySelector(`#backend-configuration-pn-adhoc-view-${view}-button`);

  /** Simulates the router landing on a URL: the snapshot is replaced, as on any navigation. */
  const landOn = (view: AdhocViewMode): void => {
    routeStub.snapshot = {firstChild: view === 'history' ? {routeConfig: {path: 'history'}} : null};
    fixture.detectChanges();
  };

  /** Exactly `view` is checked: group value plus aria-checked on both inner buttons. */
  const expectChecked = (view: AdhocViewMode): void => {
    const other: AdhocViewMode = view === 'list' ? 'history' : 'list';
    expect(group().value).toBe(view);
    expect(innerButton(view).getAttribute('aria-checked')).toBe('true');
    expect(innerButton(other).getAttribute('aria-checked')).toBe('false');
  };

  beforeEach(() => {
    routerSpy = {url: '/adhoc-tasks', navigate: jest.fn().mockResolvedValue(true)};
    routeStub = {snapshot: {firstChild: null}};

    TestBed.configureTestingModule({
      declarations: [AdhocContainerComponent],
      imports: [TranslateModule.forRoot(), MatButtonToggleModule],
      providers: [
        {provide: Router, useValue: routerSpy},
        {provide: ActivatedRoute, useValue: routeStub},
        {provide: AppMenuStateService, useValue: {leftAppMenus$: of([]), getTitleByUrl: jest.fn().mockReturnValue('Adhoc')}},
        {provide: Store, useValue: {select: jest.fn().mockReturnValue(of({})), dispatch: jest.fn()}},
        {provide: MatDialog, useValue: {open: jest.fn()}},
        {provide: Overlay, useValue: {scrollStrategies: {reposition: jest.fn().mockReturnValue({})}}},
        {
          provide: AdhocStateService,
          useValue: {
            getTasks: jest.fn().mockReturnValue(of({success: true, model: {entities: [], openCount: 0, completedCount: 0, archivedCount: 0}})),
            loadProperties: jest.fn().mockReturnValue(of([])),
            loadTags: jest.fn().mockReturnValue(of([])),
            resetReferenceData: jest.fn(),
            currentFilters: {status: 'open'},
            currentPagination: {},
          },
        },
        {provide: BackendConfigurationPnAdhocService, useValue: {getTask: jest.fn()}},
      ],
      schemas: [NO_ERRORS_SCHEMA],
    });

    fixture = TestBed.createComponent(AdhocContainerComponent);
    fixture.detectChanges();
  });

  afterEach(() => {
    fixture.destroy();
  });

  it('renders a toggle group with the ids on the mat-button-toggle hosts and <id>-button inside', () => {
    expect(group()).toBeTruthy();
    for (const view of ['list', 'history'] as const) {
      expect(toggleHost(view).tagName.toLowerCase()).toBe('mat-button-toggle');
      expect(innerButton(view)).toBeTruthy();
    }
    // The old routerLink host would have added a second tab stop (tabindex=0 on the host).
    expect(toggleHost('list').getAttribute('tabindex')).toBeNull();
  });

  it('checks Overblik on the list route', () => {
    expectChecked('list');
    expect(toggleHost('list').classList).toContain('mat-button-toggle-checked');
  });

  it('follows the route in both directions (a click, or browser back/forward)', () => {
    landOn('history');
    expectChecked('history');

    landOn('list');
    expectChecked('list');
  });

  it('clicking Historik navigates to the history child route', () => {
    innerButton('history').click();
    fixture.detectChanges();

    expect(routerSpy.navigate).toHaveBeenCalledTimes(1);
    expect(routerSpy.navigate).toHaveBeenCalledWith(['history'], {relativeTo: routeStub});
  });

  it('clicking Overblik from Historik navigates back to the container route', () => {
    landOn('history');

    innerButton('list').click();
    fixture.detectChanges();

    expect(routerSpy.navigate).toHaveBeenCalledTimes(1);
    expect(routerSpy.navigate).toHaveBeenCalledWith(['./'], {relativeTo: routeStub});
  });

  it('re-clicking the already-checked Overblik does not navigate', () => {
    innerButton('list').click();
    fixture.detectChanges();

    expect(routerSpy.navigate).not.toHaveBeenCalled();
  });
});
