import {DestroyRef, Injector, runInInjectionContext} from '@angular/core';
import {ActivatedRoute, convertToParamMap, NavigationEnd, Router} from '@angular/router';
import {Store} from '@ngrx/store';
import {BehaviorSubject, of, Subject} from 'rxjs';
import {selectCurrentUserIsFirstUser} from 'src/app/state';
import {BackendConfigurationPnPropertiesService} from '../../../../services';
import {chemicalsSelectProperty, selectChemicalsPropertyId} from '../../../../state';
import {ChemicalsContainerComponent} from './chemicals-container.component';

describe('ChemicalsContainerComponent', () => {
  let selectedProperty$: BehaviorSubject<number | null>;
  let isFirstUser$: BehaviorSubject<boolean>;
  let routerEvents$: Subject<unknown>;
  let queryParams: Record<string, string>;
  let childTitleKey: string | undefined;
  let store: {select: jest.Mock; dispatch: jest.Mock};
  let properties: {getAllPropertiesDictionary: jest.Mock};
  let router: {navigate: jest.Mock; events: Subject<unknown>};

  function build(list: Array<{id: number; name: string}>): ChemicalsContainerComponent {
    store = {
      select: jest.fn().mockImplementation((selector) =>
        selector === selectCurrentUserIsFirstUser ? isFirstUser$ : selectedProperty$),
      dispatch: jest.fn(),
    };
    properties = {getAllPropertiesDictionary: jest.fn().mockReturnValue(of({success: true, model: list}))};
    router = {navigate: jest.fn().mockResolvedValue(true), events: routerEvents$};
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
  });

  it('selects the first property when none is selected yet and writes it to the URL', () => {
    build([{id: 7, name: 'Hovedgården'}, {id: 8, name: 'Engvej 12'}]);

    expect(store.dispatch).toHaveBeenCalledWith(chemicalsSelectProperty({payload: 7}));
    expect(router.navigate).toHaveBeenCalledWith([], urlSync(7));
  });

  it('keeps a remembered property that still exists and writes it to the URL', () => {
    selectedProperty$.next(8);

    build([{id: 7, name: 'Hovedgården'}, {id: 8, name: 'Engvej 12'}]);

    expect(store.dispatch).not.toHaveBeenCalled();
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

  it.each(['99', 'abc', ''])('ignores an unusable ?propertyId=%s', (value) => {
    selectedProperty$.next(8);
    queryParams = {propertyId: value};

    build([{id: 7, name: 'Hovedgården'}, {id: 8, name: 'Engvej 12'}]);

    expect(store.dispatch).not.toHaveBeenCalled();
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
      queryParams = {};
      build([{id: 7, name: 'Hovedgården'}]);

      routerEvents$.next(new NavigationEnd(2, '/a', '/a'));

      expect(router.navigate).not.toHaveBeenCalled();
    });
  });
});
