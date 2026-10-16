import {DestroyRef, Injector, runInInjectionContext} from '@angular/core';
import {ActivatedRoute, convertToParamMap, NavigationCancel, NavigationEnd, NavigationStart, Router} from '@angular/router';
import {Store} from '@ngrx/store';
import {BehaviorSubject, of, Subject, throwError} from 'rxjs';
import {selectCurrentUserIsFirstUser} from 'src/app/state';
import {BackendConfigurationPnPropertiesService} from '../../../../services';
import {chemicalsSelectProperty, chemicalsSetProperties, selectChemicalsPropertyId} from '../../../../state';
import {ChemicalsContainerComponent} from './chemicals-container.component';

describe('ChemicalsContainerComponent', () => {
  let selectedProperty$: BehaviorSubject<number | null>;
  let isFirstUser$: BehaviorSubject<boolean>;
  let routerEvents$: Subject<unknown>;
  let queryParams: Record<string, string>;
  let childTitleKey: string | undefined;
  let store: {select: jest.Mock; dispatch: jest.Mock};
  let properties: {getAllPropertiesDictionary: jest.Mock};
  let router: {navigate: jest.Mock; events: Subject<unknown>; currentNavigation: jest.Mock};
  /** What the Router.currentNavigation signal holds when the container starts. */
  let navigationAtStart: unknown;

  function build(list: Array<{id: number; name: string}>, response?: unknown): ChemicalsContainerComponent {
    store = {
      select: jest.fn().mockImplementation((selector) =>
        selector === selectCurrentUserIsFirstUser ? isFirstUser$ : selectedProperty$),
      // The real store echoes a selection back through selectChemicalsPropertyId.
      dispatch: jest.fn().mockImplementation((action: {type: string; payload: number | null}) => {
        if (action.type === chemicalsSelectProperty.type) {
          selectedProperty$.next(action.payload);
        }
      }),
    };
    properties = {getAllPropertiesDictionary: jest.fn().mockReturnValue(response ?? of({success: true, model: list}))};
    router = {
      navigate: jest.fn().mockResolvedValue(true),
      events: routerEvents$,
      currentNavigation: jest.fn().mockReturnValue(navigationAtStart),
    };
    const route = {
      get snapshot() {
        return {queryParamMap: convertToParamMap(queryParams)};
      },
      get firstChild() {
        return childTitleKey ? {snapshot: {data: {titleKey: childTitleKey}}} : null;
      },
    };
    const injector = Injector.create({
      providers: [
        {provide: Store, useValue: store},
        {provide: BackendConfigurationPnPropertiesService, useValue: properties},
        {provide: DestroyRef, useValue: {onDestroy: jest.fn()}},
        {provide: Router, useValue: router},
        {provide: ActivatedRoute, useValue: route},
      ],
    });
    const component = runInInjectionContext(injector, () => new ChemicalsContainerComponent());
    component.ngOnInit();
    return component;
  }

  const urlSync = (id: number) => ({queryParams: {propertyId: id}, queryParamsHandling: 'merge', replaceUrl: true});

  beforeEach(() => {
    selectedProperty$ = new BehaviorSubject<number | null>(null);
    isFirstUser$ = new BehaviorSubject<boolean>(true);
    routerEvents$ = new Subject<unknown>();
    queryParams = {};
    childTitleKey = undefined;
    navigationAtStart = null;
  });

  it('selects the first property when none is selected yet and writes it to the URL', () => {
    build([{id: 7, name: 'Hovedgården'}, {id: 8, name: 'Engvej 12'}]);

    expect(store.dispatch).toHaveBeenCalledWith(chemicalsSelectProperty({payload: 7}));
    expect(router.navigate).toHaveBeenCalledWith([], urlSync(7));
  });

  it('keeps a remembered property that still exists and writes it to the URL', () => {
    selectedProperty$.next(8);

    build([{id: 7, name: 'Hovedgården'}, {id: 8, name: 'Engvej 12'}]);

    expect(store.dispatch).not.toHaveBeenCalledWith(expect.objectContaining({type: chemicalsSelectProperty.type}));
    expect(router.navigate).toHaveBeenCalledWith([], urlSync(8));
  });

  it('restores the property from ?propertyId= (a reload loses the store)', () => {
    queryParams = {propertyId: '8'};

    const component = build([{id: 7, name: 'Hovedgården'}, {id: 8, name: 'Engvej 12'}]);

    expect(store.dispatch).toHaveBeenCalledWith(chemicalsSelectProperty({payload: 8}));
    expect(component.propertyId).toBe(8);
    expect(router.navigate).not.toHaveBeenCalled();
  });

  it('lets ?propertyId= win over the remembered property', () => {
    selectedProperty$.next(7);
    queryParams = {propertyId: '8'};

    build([{id: 7, name: 'Hovedgården'}, {id: 8, name: 'Engvej 12'}]);

    expect(store.dispatch).toHaveBeenCalledWith(chemicalsSelectProperty({payload: 8}));
  });

  it.each(['99', 'abc', '', '0', '-3', '1.5'])('ignores an unusable ?propertyId=%s', (value) => {
    selectedProperty$.next(8);
    queryParams = {propertyId: value};

    build([{id: 7, name: 'Hovedgården'}, {id: 8, name: 'Engvej 12'}]);

    expect(store.dispatch).not.toHaveBeenCalledWith(expect.objectContaining({type: chemicalsSelectProperty.type}));
    expect(router.navigate).toHaveBeenCalledWith([], urlSync(8));
  });

  it('replaces a remembered property that no longer exists', () => {
    selectedProperty$.next(99);

    build([{id: 7, name: 'Hovedgården'}]);

    expect(store.dispatch).toHaveBeenCalledWith(chemicalsSelectProperty({payload: 7}));
  });

  it('clears the selection when the customer has no properties, without touching the URL', () => {
    selectedProperty$.next(99);

    const component = build([]);

    expect(store.dispatch).toHaveBeenCalledWith(chemicalsSelectProperty({payload: null}));
    expect(component.properties).toEqual([]);
    expect(router.navigate).not.toHaveBeenCalled();
  });

  it('dispatches a picked property and writes it to the URL', () => {
    const component = build([{id: 7, name: 'Hovedgården'}, {id: 9, name: 'Østergade 1'}]);
    store.dispatch.mockClear();
    router.navigate.mockClear();

    component.onPropertyChange(9);

    expect(store.dispatch).toHaveBeenCalledWith(chemicalsSelectProperty({payload: 9}));
    expect(router.navigate).toHaveBeenCalledWith([], urlSync(9));
  });

  it('puts the property back on the URL after a menu click dropped the query parameter', () => {
    queryParams = {propertyId: '7'};
    build([{id: 7, name: 'Hovedgården'}]);
    expect(router.navigate).not.toHaveBeenCalled();

    queryParams = {};
    routerEvents$.next(new NavigationEnd(2, '/plugins/backend-configuration-pn/chemicals/permissions', '/x'));

    expect(router.navigate).toHaveBeenCalledWith([], urlSync(7));
  });

  it('shows the title of the active screen and the Kemi title before one is active', () => {
    const component = build([{id: 7, name: 'Hovedgården'}]);
    expect(component.titleKey).toBe('Chemicals: Title');

    childTitleKey = 'Chemicals: Permissions title';
    routerEvents$.next(new NavigationEnd(3, '/a', '/a'));

    expect(component.titleKey).toBe('Chemicals: Permissions title');
  });

  it('adopts a different VALID ?propertyId= on a later navigation instead of reverting it (I-1)', () => {
    queryParams = {propertyId: '7'};
    const component = build([{id: 7, name: 'Hovedgården'}, {id: 8, name: 'Engvej 12'}]);
    store.dispatch.mockClear();
    router.navigate.mockClear();

    queryParams = {propertyId: '8'};
    routerEvents$.next(new NavigationEnd(4, '/a', '/a'));

    expect(store.dispatch).toHaveBeenCalledWith(chemicalsSelectProperty({payload: 8}));
    expect(component.propertyId).toBe(8);
    expect(router.navigate).not.toHaveBeenCalled();

    // A second NavigationEnd with the same URL changes nothing: no loop.
    store.dispatch.mockClear();
    routerEvents$.next(new NavigationEnd(5, '/a', '/a'));
    expect(store.dispatch).not.toHaveBeenCalled();
    expect(router.navigate).not.toHaveBeenCalled();
  });

  it('still repairs an UNKNOWN ?propertyId= on a later navigation', () => {
    queryParams = {propertyId: '7'};
    build([{id: 7, name: 'Hovedgården'}]);
    router.navigate.mockClear();
    store.dispatch.mockClear();

    queryParams = {propertyId: '99'};
    routerEvents$.next(new NavigationEnd(4, '/a', '/a'));

    expect(store.dispatch).not.toHaveBeenCalled();
    expect(router.navigate).toHaveBeenCalledWith([], urlSync(7));
  });

  it('publishes the loaded list to the store, so the screens do not fetch it again', () => {
    const list = [{id: 7, name: 'Hovedgården', description: ''}, {id: 8, name: 'Engvej 12', description: ''}];

    build(list);

    expect(properties.getAllPropertiesDictionary).toHaveBeenCalledTimes(1);
    expect(store.dispatch).toHaveBeenCalledWith(chemicalsSetProperties({payload: list}));
  });

  it('follows the first-user flag instead of freezing it (M-1)', () => {
    isFirstUser$.next(false);
    const component = build([{id: 7, name: 'Hovedgården'}]);
    expect(properties.getAllPropertiesDictionary).not.toHaveBeenCalled();

    isFirstUser$.next(true);

    expect(component.isFirstUser).toBe(true);
    expect(properties.getAllPropertiesDictionary).toHaveBeenCalledTimes(1);
    expect(store.dispatch).toHaveBeenCalledWith(chemicalsSelectProperty({payload: 7}));
  });

  it('stops touching the URL when the first-user flag is withdrawn', () => {
    queryParams = {propertyId: '7'};
    build([{id: 7, name: 'Hovedgården'}]);
    router.navigate.mockClear();
    store.dispatch.mockClear();

    isFirstUser$.next(false);
    queryParams = {};
    routerEvents$.next(new NavigationEnd(2, '/a', '/a'));

    expect(router.navigate).not.toHaveBeenCalled();
    expect(store.dispatch).not.toHaveBeenCalled();
  });

  describe('a property switch with unsaved edits on the screen (I-3)', () => {
    function withScreen(answer: boolean) {
      const component = build([{id: 7, name: 'Hovedgården'}, {id: 8, name: 'Engvej 12'}]);
      const screen = {confirmDiscardChanges: jest.fn().mockReturnValue(of(answer))};
      component.onScreenActivate(screen);
      store.dispatch.mockClear();
      router.navigate.mockClear();
      return {component, screen};
    }

    it('switches after the screen agreed', () => {
      const {component, screen} = withScreen(true);

      component.onPropertyChange(8);

      expect(screen.confirmDiscardChanges).toHaveBeenCalledTimes(1);
      expect(store.dispatch).toHaveBeenCalledWith(chemicalsSelectProperty({payload: 8}));
      expect(component.pickerValue).toBe(8);
    });

    it('stays on the current property and puts the picker back on cancel', () => {
      const {component} = withScreen(false);

      jest.useFakeTimers();

      component.onPropertyChange(8);

      expect(store.dispatch).not.toHaveBeenCalled();
      expect(component.propertyId).toBe(7);
      // Put back after a change-detection pass, so the select re-renders the old property.
      expect(component.pickerValue).toBe(8);
      jest.runAllTimers();
      jest.useRealTimers();
      expect(component.pickerValue).toBe(7);
      expect(router.navigate).not.toHaveBeenCalled();
    });

    it('asks before adopting another ?propertyId= and repairs the URL on cancel', () => {
      const {component, screen} = withScreen(false);

      queryParams = {propertyId: '8'};
      routerEvents$.next(new NavigationEnd(4, '/a', '/a'));

      expect(screen.confirmDiscardChanges).toHaveBeenCalledTimes(1);
      expect(store.dispatch).not.toHaveBeenCalled();
      expect(component.propertyId).toBe(7);
      expect(router.navigate).toHaveBeenCalledWith([], urlSync(7));
    });

    it('does not ask when the picked property is the current one', () => {
      const {component, screen} = withScreen(false);

      component.onPropertyChange(7);

      expect(screen.confirmDiscardChanges).not.toHaveBeenCalled();
    });

    it('stops asking a screen that was deactivated', () => {
      const {component, screen} = withScreen(false);
      component.onScreenDeactivate();

      component.onPropertyChange(8);

      expect(screen.confirmDiscardChanges).not.toHaveBeenCalled();
      expect(store.dispatch).toHaveBeenCalledWith(chemicalsSelectProperty({payload: 8}));
    });
  });

  it('ignores a properties answer that arrives after the first-user flag was withdrawn (R-1)', () => {
    const pending = new Subject<unknown>();
    const component = build([], pending);

    isFirstUser$.next(false);
    pending.next({success: true, model: [{id: 7, name: 'Hovedgården'}]});

    expect(component.properties).toEqual([]);
    expect(store.dispatch).not.toHaveBeenCalled();
    expect(router.navigate).not.toHaveBeenCalled();
  });

  it.each([
    ['an unsuccessful answer', of({success: false, model: null})],
    ['a failed request', throwError(() => new Error('boom'))],
  ])('shows an error state, not "no properties", after %s (M-2)', (_name, response) => {
    const component = build([], response);

    expect(component.loadFailed).toBe(true);
    expect(component.properties).toEqual([]);
    expect(store.dispatch).not.toHaveBeenCalled();
    expect(router.navigate).not.toHaveBeenCalled();
  });

  it('does not flag a successful empty list as a failure', () => {
    expect(build([]).loadFailed).toBe(false);
  });

  describe('for anyone but the first user (E-05, until GA)', () => {
    beforeEach(() => {
      isFirstUser$.next(false);
    });

    it('makes no chemistry call and dispatches nothing', () => {
      const component = build([{id: 7, name: 'Hovedgården'}]);

      expect(component.isFirstUser).toBe(false);
      expect(properties.getAllPropertiesDictionary).not.toHaveBeenCalled();
      expect(store.dispatch).not.toHaveBeenCalled();
      expect(router.navigate).not.toHaveBeenCalled();
    });

    it('does not rewrite the URL on navigation either', () => {
      build([{id: 7, name: 'Hovedgården'}]);

      routerEvents$.next(new NavigationEnd(2, '/a', '/a'));

      expect(router.navigate).not.toHaveBeenCalled();
    });
  });
  /**
   * R-2: W1 is the landing screen, so the first properties answer often lands while the user's
   * first menu click is still navigating. A URL write then would replace (cancel) that click.
   */
  describe('never cancels a navigation in flight with its ?propertyId= write (R-2)', () => {
    const permissionsUrl = '/plugins/backend-configuration-pn/chemicals/permissions';

    afterEach(() => {
      jest.useRealTimers();
    });

    it('waits for the navigation to end, then writes the URL', () => {
      const pending = new Subject<unknown>();
      build([], pending);

      routerEvents$.next(new NavigationStart(2, permissionsUrl));
      pending.next({success: true, model: [{id: 7, name: 'Hovedgården'}]});

      expect(store.dispatch).toHaveBeenCalledWith(chemicalsSelectProperty({payload: 7}));
      expect(router.navigate).not.toHaveBeenCalled();

      routerEvents$.next(new NavigationEnd(2, permissionsUrl, permissionsUrl));

      expect(router.navigate).toHaveBeenCalledTimes(1);
      expect(router.navigate).toHaveBeenCalledWith([], urlSync(7));
    });

    it('also waits when the container starts inside the navigation that created it', () => {
      navigationAtStart = {id: 1};

      build([{id: 7, name: 'Hovedgården'}]);

      expect(router.navigate).not.toHaveBeenCalled();

      routerEvents$.next(new NavigationEnd(1, '/plugins/backend-configuration-pn/chemicals', '/plugins/backend-configuration-pn/chemicals'));

      expect(router.navigate).toHaveBeenCalledWith([], urlSync(7));
    });

    it('writes the URL after a cancelled navigation once nothing else is navigating', () => {
      jest.useFakeTimers();
      const pending = new Subject<unknown>();
      build([], pending);
      routerEvents$.next(new NavigationStart(2, permissionsUrl));
      pending.next({success: true, model: [{id: 7, name: 'Hovedgården'}]});

      routerEvents$.next(new NavigationCancel(2, permissionsUrl, 'guard said no'));

      expect(router.navigate).not.toHaveBeenCalled();
      jest.runOnlyPendingTimers();
      expect(router.navigate).toHaveBeenCalledWith([], urlSync(7));
    });

    it('leaves a superseding navigation alone and writes after it ends', () => {
      jest.useFakeTimers();
      const pending = new Subject<unknown>();
      build([], pending);
      routerEvents$.next(new NavigationStart(2, permissionsUrl));
      pending.next({success: true, model: [{id: 7, name: 'Hovedgården'}]});

      routerEvents$.next(new NavigationCancel(2, permissionsUrl, 'superseded'));
      routerEvents$.next(new NavigationStart(3, '/plugins/backend-configuration-pn/chemicals/settings'));
      jest.runOnlyPendingTimers();

      expect(router.navigate).not.toHaveBeenCalled();

      routerEvents$.next(new NavigationEnd(3, '/plugins/backend-configuration-pn/chemicals/settings', '/plugins/backend-configuration-pn/chemicals/settings'));
      expect(router.navigate).toHaveBeenCalledWith([], urlSync(7));
    });

    it('still writes a picked property at once when nothing is navigating', () => {
      const component = build([{id: 7, name: 'Hovedgården'}, {id: 8, name: 'Engvej 12'}]);
      router.navigate.mockClear();

      component.onPropertyChange(8);

      expect(router.navigate).toHaveBeenCalledWith([], urlSync(8));
    });
  });
});
