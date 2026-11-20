import {describe, it, expect, beforeEach, afterEach, vi} from 'vitest';
import type {Mock} from 'vitest';
import {DestroyRef, Injector, runInInjectionContext} from '@angular/core';
import {ActivatedRoute, convertToParamMap, NavigationCancel, NavigationEnd, NavigationStart, Router} from '@angular/router';
import {Store} from '@ngrx/store';
import {BehaviorSubject, of, Subject, throwError} from 'rxjs';
import {selectCurrentUserIsFirstUser} from 'src/app/state';
import {TranslateService} from '@ngx-translate/core';
import {BackendConfigurationPnPropertiesService} from '../../../../services';
import {
  ChemicalsScope,
  chemicalsSelectAllProperties,
  chemicalsSelectProperty,
  chemicalsSetProperties,
  selectChemicalsScope,
} from '../../../../state';
import {CHEMICALS_ALL_PROPERTIES, ChemicalsContainerComponent} from './chemicals-container.component';

describe('ChemicalsContainerComponent', () => {
  /** The store's chemicalsState scope: the selected property, or "All". */
  let scope$: BehaviorSubject<ChemicalsScope>;
  /** The routed screen offers "All" (route data allProperties: Beholdning, Historik). */
  let childAllowsAll: boolean;
  let isFirstUser$: BehaviorSubject<boolean>;
  let routerEvents$: Subject<unknown>;
  let queryParams: Record<string, string>;
  let childTitleKey: string | undefined;
  let store: {select: Mock; dispatch: Mock};
  let properties: {getAllPropertiesDictionary: Mock};
  let router: {navigate: Mock; events: Subject<unknown>; currentNavigation: Mock};
  /** What the Router.currentNavigation signal holds when the container starts. */
  let navigationAtStart: unknown;

  function build(list: Array<{id: number; name: string}>, response?: unknown): ChemicalsContainerComponent {
    store = {
      select: vi.fn().mockImplementation((selector) => {
        if (selector === selectCurrentUserIsFirstUser) {
          return isFirstUser$;
        }
        if (selector === selectChemicalsScope) {
          return scope$;
        }
        throw new Error('the container reads the selection through selectChemicalsScope');
      }),
      // The real store echoes a selection back through selectChemicalsScope.
      dispatch: vi.fn().mockImplementation((action: {type: string; payload: number | null}) => {
        if (action.type === chemicalsSelectProperty.type) {
          scope$.next({propertyId: action.payload, allProperties: false});
        } else if (action.type === chemicalsSelectAllProperties.type) {
          scope$.next({propertyId: null, allProperties: true});
        }
      }),
    };
    properties = {getAllPropertiesDictionary: vi.fn().mockReturnValue(response ?? of({success: true, model: list}))};
    router = {
      navigate: vi.fn().mockResolvedValue(true),
      events: routerEvents$,
      currentNavigation: vi.fn().mockReturnValue(navigationAtStart),
    };
    const route = {
      get snapshot() {
        return {queryParamMap: convertToParamMap(queryParams)};
      },
      get firstChild() {
        return childTitleKey || childAllowsAll
          ? {snapshot: {data: {titleKey: childTitleKey, allProperties: childAllowsAll}}}
          : null;
      },
    };
    const injector = Injector.create({
      providers: [
        {provide: Store, useValue: store},
        {provide: BackendConfigurationPnPropertiesService, useValue: properties},
        {provide: DestroyRef, useValue: {onDestroy: vi.fn()}},
        {provide: Router, useValue: router},
        {provide: ActivatedRoute, useValue: route},
        {provide: TranslateService, useValue: {stream: (key: string) => of(key)}},
      ],
    });
    const component = runInInjectionContext(injector, () => new ChemicalsContainerComponent());
    component.ngOnInit();
    return component;
  }

  /** A property the store still holds from an earlier visit. */
  const remember = (propertyId: number) => scope$.next({propertyId, allProperties: false});

  const urlSync = (id: number) => ({queryParams: {propertyId: id}, queryParamsHandling: 'merge', replaceUrl: true});

  beforeEach(() => {
    scope$ = new BehaviorSubject<ChemicalsScope>({propertyId: null, allProperties: false});
    childAllowsAll = false;
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
    remember(8);

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
    remember(7);
    queryParams = {propertyId: '8'};

    build([{id: 7, name: 'Hovedgården'}, {id: 8, name: 'Engvej 12'}]);

    expect(store.dispatch).toHaveBeenCalledWith(chemicalsSelectProperty({payload: 8}));
  });

  it.each(['99', 'abc', '', '0', '-3', '1.5'])('ignores an unusable ?propertyId=%s', (value) => {
    remember(8);
    queryParams = {propertyId: value};

    build([{id: 7, name: 'Hovedgården'}, {id: 8, name: 'Engvej 12'}]);

    expect(store.dispatch).not.toHaveBeenCalledWith(expect.objectContaining({type: chemicalsSelectProperty.type}));
    expect(router.navigate).toHaveBeenCalledWith([], urlSync(8));
  });

  it('replaces a remembered property that no longer exists', () => {
    remember(99);

    build([{id: 7, name: 'Hovedgården'}]);

    expect(store.dispatch).toHaveBeenCalledWith(chemicalsSelectProperty({payload: 7}));
  });

  it('clears the selection when the customer has no properties, without touching the URL', () => {
    remember(99);

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
      const screen = {confirmDiscardChanges: vi.fn().mockReturnValue(of(answer))};
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

      vi.useFakeTimers();

      component.onPropertyChange(8);

      expect(store.dispatch).not.toHaveBeenCalled();
      expect(component.propertyId).toBe(7);
      // Put back after a change-detection pass, so the select re-renders the old property.
      expect(component.pickerValue).toBe(8);
      vi.runAllTimers();
      vi.useRealTimers();
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
      vi.useRealTimers();
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
      vi.useFakeTimers();
      const pending = new Subject<unknown>();
      build([], pending);
      routerEvents$.next(new NavigationStart(2, permissionsUrl));
      pending.next({success: true, model: [{id: 7, name: 'Hovedgården'}]});

      routerEvents$.next(new NavigationCancel(2, permissionsUrl, 'guard said no'));

      expect(router.navigate).not.toHaveBeenCalled();
      vi.runOnlyPendingTimers();
      expect(router.navigate).toHaveBeenCalledWith([], urlSync(7));
    });

    it('leaves a superseding navigation alone and writes after it ends', () => {
      vi.useFakeTimers();
      const pending = new Subject<unknown>();
      build([], pending);
      routerEvents$.next(new NavigationStart(2, permissionsUrl));
      pending.next({success: true, model: [{id: 7, name: 'Hovedgården'}]});

      routerEvents$.next(new NavigationCancel(2, permissionsUrl, 'superseded'));
      routerEvents$.next(new NavigationStart(3, '/plugins/backend-configuration-pn/chemicals/settings'));
      vi.runOnlyPendingTimers();

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
  it('offers no "All" on Lokationer, Rettigheder and Indstillinger', () => {
    childTitleKey = 'Chemicals: Locations title';

    const component = build([{id: 7, name: 'Hovedgården'}, {id: 8, name: 'Engvej 12'}]);

    expect(component.pickerItems.map((i) => i.id)).toEqual([7, 8]);
    expect(store.dispatch).toHaveBeenCalledWith(chemicalsSelectProperty({payload: 7}));
    expect(store.dispatch).not.toHaveBeenCalledWith(chemicalsSelectAllProperties());
  });

  describe('"All" on Beholdning and Historik (owner 2026-11-20)', () => {
    const list = [{id: 7, name: 'Hovedgården'}, {id: 8, name: 'Engvej 12'}];
    const dropParam = {queryParams: {propertyId: null}, queryParamsHandling: 'merge', replaceUrl: true};

    beforeEach(() => {
      childAllowsAll = true;
    });

    it('offers "All" first and opens on it without a ?propertyId=', () => {
      const component = build(list);

      expect(component.pickerItems.map((i) => i.id)).toEqual([CHEMICALS_ALL_PROPERTIES, 7, 8]);
      expect(component.pickerItems[0].name).toBe('Chemicals: All');
      expect(store.dispatch).toHaveBeenCalledWith(chemicalsSelectAllProperties());
      expect(component.allProperties).toBe(true);
      expect(component.propertyId).toBeNull();
      expect(component.pickerValue).toBe(CHEMICALS_ALL_PROPERTIES);
      // "All" is the URL without ?propertyId=: nothing to write.
      expect(router.navigate).not.toHaveBeenCalled();
    });

    it('opens on "All" even when the store remembers a property', () => {
      remember(8);

      build(list);

      expect(store.dispatch).toHaveBeenCalledWith(chemicalsSelectAllProperties());
    });

    it('still opens the one property of a ?propertyId= (the digest deeplink)', () => {
      queryParams = {propertyId: '8', filter: 'requires-action'};

      const component = build(list);

      expect(store.dispatch).toHaveBeenCalledWith(chemicalsSelectProperty({payload: 8}));
      expect(store.dispatch).not.toHaveBeenCalledWith(chemicalsSelectAllProperties());
      expect(component.pickerValue).toBe(8);
      expect(router.navigate).not.toHaveBeenCalled();
    });

    it('picking "All" drops ?propertyId= from the URL; picking a property writes it again', () => {
      queryParams = {propertyId: '7'};
      const component = build(list);
      store.dispatch.mockClear();
      router.navigate.mockClear();

      component.onPropertyChange(CHEMICALS_ALL_PROPERTIES);

      expect(store.dispatch).toHaveBeenCalledWith(chemicalsSelectAllProperties());
      expect(router.navigate).toHaveBeenCalledWith([], dropParam);

      queryParams = {};
      router.navigate.mockClear();
      component.onPropertyChange(8);

      expect(store.dispatch).toHaveBeenCalledWith(chemicalsSelectProperty({payload: 8}));
      expect(router.navigate).toHaveBeenCalledWith([], urlSync(8));
    });

    it('does not ask the screen again when "All" is picked while "All" is selected', () => {
      const component = build(list);
      const screen = {confirmDiscardChanges: vi.fn().mockReturnValue(of(true))};
      component.onScreenActivate(screen);
      store.dispatch.mockClear();

      component.onPropertyChange(CHEMICALS_ALL_PROPERTIES);

      expect(screen.confirmDiscardChanges).not.toHaveBeenCalled();
      expect(store.dispatch).not.toHaveBeenCalled();
    });

    it('asks the screen before switching to "All" and puts the picker back on "keep editing"', () => {
      queryParams = {propertyId: '7'};
      const component = build(list);
      component.onScreenActivate({confirmDiscardChanges: () => of(false)});
      store.dispatch.mockClear();
      vi.useFakeTimers();

      component.onPropertyChange(CHEMICALS_ALL_PROPERTIES);
      vi.runAllTimers();
      vi.useRealTimers();

      expect(store.dispatch).not.toHaveBeenCalled();
      expect(component.pickerValue).toBe(7);
    });

    it('keeps "All" when a menu click lands on the other screen with "All" without a query parameter', () => {
      const component = build(list);
      store.dispatch.mockClear();
      router.navigate.mockClear();

      routerEvents$.next(new NavigationEnd(2, '/chemicals/history', '/chemicals/history'));

      expect(store.dispatch).not.toHaveBeenCalled();
      expect(router.navigate).not.toHaveBeenCalled();
      expect(component.pickerValue).toBe(CHEMICALS_ALL_PROPERTIES);
    });

    it('returns to "All" when a menu click or Back lands on Beholdning/Historik without ?propertyId= (brief: no param = All)', () => {
      queryParams = {propertyId: '7'};
      const component = build(list);
      store.dispatch.mockClear();
      router.navigate.mockClear();

      queryParams = {};
      routerEvents$.next(new NavigationEnd(2, '/chemicals/history', '/chemicals/history'));

      expect(store.dispatch).toHaveBeenCalledWith(chemicalsSelectAllProperties());
      expect(component.pickerValue).toBe(CHEMICALS_ALL_PROPERTIES);
      // "All" is the URL without the parameter: nothing to write back.
      expect(router.navigate).not.toHaveBeenCalled();
    });

    it('returns to "All" when coming back from a screen without it', () => {
      const component = build(list);
      childAllowsAll = false;
      routerEvents$.next(new NavigationEnd(2, '/chemicals/locations', '/chemicals/locations'));
      expect(component.propertyId).toBe(7);
      store.dispatch.mockClear();

      childAllowsAll = true;
      queryParams = {};
      routerEvents$.next(new NavigationEnd(3, '/chemicals/inventory', '/chemicals/inventory'));

      expect(store.dispatch).toHaveBeenCalledWith(chemicalsSelectAllProperties());
    });

    it('asks the screen before a navigation without ?propertyId= switches to "All", and keeps the property on "keep editing"', () => {
      queryParams = {propertyId: '7'};
      const component = build(list);
      component.onScreenActivate({confirmDiscardChanges: () => of(false)});
      store.dispatch.mockClear();
      router.navigate.mockClear();

      queryParams = {};
      routerEvents$.next(new NavigationEnd(2, '/chemicals/inventory', '/chemicals/inventory'));

      expect(store.dispatch).not.toHaveBeenCalled();
      expect(component.propertyId).toBe(7);
      expect(router.navigate).toHaveBeenCalledWith([], urlSync(7));
    });

    it('keeps a property picked while a navigation was in flight, and writes it once the navigation ends (R-2)', () => {
      const component = build(list);
      routerEvents$.next(new NavigationStart(2, '/chemicals/history'));
      router.navigate.mockClear();

      component.onPropertyChange(8);
      expect(router.navigate).not.toHaveBeenCalled();
      store.dispatch.mockClear();
      routerEvents$.next(new NavigationEnd(2, '/chemicals/history', '/chemicals/history'));

      expect(store.dispatch).not.toHaveBeenCalledWith(chemicalsSelectAllProperties());
      expect(component.propertyId).toBe(8);
      expect(router.navigate).toHaveBeenCalledWith([], urlSync(8));
    });

    describe('a pick made while a navigation to ?propertyId=7 is in flight wins when it ends (Copilot 4640955472)', () => {
      it('picking property 8', () => {
        const component = build(list);
        routerEvents$.next(new NavigationStart(2, '/chemicals/inventory?propertyId=7'));

        component.onPropertyChange(8);
        store.dispatch.mockClear();
        router.navigate.mockClear();
        queryParams = {propertyId: '7'};
        routerEvents$.next(new NavigationEnd(2, '/chemicals/inventory?propertyId=7', '/chemicals/inventory?propertyId=7'));

        expect(store.dispatch).not.toHaveBeenCalled();
        expect(component.propertyId).toBe(8);
        expect(router.navigate).toHaveBeenCalledWith([], urlSync(8));
      });

      it('picking "All"', () => {
        queryParams = {propertyId: '8'};
        const component = build(list);
        routerEvents$.next(new NavigationStart(2, '/chemicals/inventory?propertyId=7'));

        component.onPropertyChange(CHEMICALS_ALL_PROPERTIES);
        store.dispatch.mockClear();
        router.navigate.mockClear();
        queryParams = {propertyId: '7'};
        routerEvents$.next(new NavigationEnd(2, '/chemicals/inventory?propertyId=7', '/chemicals/inventory?propertyId=7'));

        expect(store.dispatch).not.toHaveBeenCalled();
        expect(component.allProperties).toBe(true);
        expect(router.navigate).toHaveBeenCalledWith([], dropParam);
      });
    });

    it('the first-load selection is no pick: a navigation to ?propertyId=8 that supersedes the first one wins', () => {
      vi.useFakeTimers();
      const pending = new Subject<unknown>();
      const component = build([], pending);
      routerEvents$.next(new NavigationStart(2, '/chemicals/inventory'));
      // The properties answer lands mid-navigation: the starting selection ("All") is applied then.
      pending.next({success: true, model: list});
      expect(store.dispatch).toHaveBeenCalledWith(chemicalsSelectAllProperties());
      store.dispatch.mockClear();

      routerEvents$.next(new NavigationCancel(2, '/chemicals/inventory', 'superseded'));
      routerEvents$.next(new NavigationStart(3, '/chemicals/inventory?propertyId=8'));
      vi.runOnlyPendingTimers();
      queryParams = {propertyId: '8'};
      routerEvents$.next(new NavigationEnd(3, '/chemicals/inventory?propertyId=8', '/chemicals/inventory?propertyId=8'));
      vi.useRealTimers();

      expect(store.dispatch).toHaveBeenCalledWith(chemicalsSelectProperty({payload: 8}));
      expect(component.propertyId).toBe(8);
      expect(component.allProperties).toBe(false);
      // The URL already says 8: nothing to write.
      expect(router.navigate).not.toHaveBeenCalled();
    });

    it('a cancelled navigation replaced by a newer one does not count as a pending pick (rereview)', () => {
      vi.useFakeTimers();
      queryParams = {propertyId: '7'};
      const component = build(list);
      store.dispatch.mockClear();
      router.navigate.mockClear();

      routerEvents$.next(new NavigationStart(2, '/chemicals/history?propertyId=7'));
      routerEvents$.next(new NavigationCancel(2, '/chemicals/history?propertyId=7', 'superseded'));
      routerEvents$.next(new NavigationStart(3, '/chemicals/history'));
      vi.runOnlyPendingTimers();
      queryParams = {};
      routerEvents$.next(new NavigationEnd(3, '/chemicals/history', '/chemicals/history'));
      vi.useRealTimers();

      // The newer navigation has no ?propertyId= on a screen with "All": "All", as for any menu click.
      expect(store.dispatch).toHaveBeenCalledWith(chemicalsSelectAllProperties());
      expect(component.allProperties).toBe(true);
      expect(router.navigate).not.toHaveBeenCalled();
    });

    it('adopts the ?propertyId= of a later navigation while "All" is selected', () => {
      build(list);
      store.dispatch.mockClear();

      queryParams = {propertyId: '8'};
      routerEvents$.next(new NavigationEnd(2, '/chemicals/inventory?propertyId=8', '/chemicals/inventory?propertyId=8'));

      expect(store.dispatch).toHaveBeenCalledWith(chemicalsSelectProperty({payload: 8}));
    });

    it('selects the first property when the user moves on to a screen without "All"', () => {
      const component = build(list);
      store.dispatch.mockClear();
      router.navigate.mockClear();

      childAllowsAll = false;
      routerEvents$.next(new NavigationEnd(2, '/chemicals/locations', '/chemicals/locations'));

      expect(component.pickerItems.map((i) => i.id)).toEqual([7, 8]);
      expect(store.dispatch).toHaveBeenCalledWith(chemicalsSelectProperty({payload: 7}));
      expect(router.navigate).toHaveBeenCalledWith([], urlSync(7));
    });

    it('selects nothing, not "All", when the customer has no properties', () => {
      remember(99);

      const component = build([]);

      expect(store.dispatch).toHaveBeenCalledWith(chemicalsSelectProperty({payload: null}));
      expect(store.dispatch).not.toHaveBeenCalledWith(chemicalsSelectAllProperties());
      expect(component.allProperties).toBe(false);
    });
  });
});
