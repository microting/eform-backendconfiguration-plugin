import {TestBed} from '@angular/core/testing';
import {NavigationEnd, Router} from '@angular/router';
import {Subject, of} from 'rxjs';
import {BackendConfigurationPnTailBiteService} from '../../../../services';
import {TailBiteShellComponent} from './tail-bite-shell.component';

describe('TailBiteShellComponent', () => {
  const base = '/plugins/backend-configuration-pn/tail-bite';
  let router: {url: string; events: Subject<unknown>; navigate: jest.Mock};
  let getProperties: jest.Mock;
  let component: TailBiteShellComponent;

  const create = (url: string) => {
    router = {url, events: new Subject(), navigate: jest.fn()};
    TestBed.configureTestingModule({
      providers: [
        {provide: Router, useValue: router},
        {provide: BackendConfigurationPnTailBiteService, useValue: {getProperties}},
      ],
    });
    component = TestBed.runInInjectionContext(() => new TailBiteShellComponent());
    component.ngOnInit();
  };

  beforeEach(() => {
    getProperties = jest.fn().mockReturnValue(of({success: true, model: [
      {propertyId: 1, name: 'Ejendom Nord', enabled: true},
      {propertyId: 2, name: 'Ejendom Syd', enabled: false},
      {propertyId: 3, name: 'Ejendom Vest', enabled: true},
    ]}));
  });

  afterEach(() => component.ngOnDestroy());

  it('offers only enabled properties and opens the first one from the bare URL', () => {
    create(base);
    expect(component.properties!.map((p) => p.propertyId)).toEqual([1, 3]);
    expect(router.navigate).toHaveBeenCalledWith([base, 1, 'outbreaks'], {replaceUrl: true});
  });

  it('takes the property from the URL and follows later navigations', () => {
    create(`${base}/3/rules`);
    expect(component.propertyId).toBe(3);
    expect(router.navigate).not.toHaveBeenCalled();
    router.url = `${base}/1/rules`;
    router.events.next(new NavigationEnd(1, router.url, router.url));
    expect(component.propertyId).toBe(1);
  });

  it('switches property on the same tab, and from an outbreak page to the new list', () => {
    create(`${base}/3/locations`);
    component.select(1);
    expect(router.navigate).toHaveBeenLastCalledWith([base, 1, 'locations']);
    router.url = `${base}/3/outbreaks/12`;
    component.select(1);
    expect(router.navigate).toHaveBeenLastCalledWith([base, 1, 'outbreaks']);
  });

  it('redirects an unknown or disabled property to the first one, keeping the tab', () => {
    create(`${base}/2/rules`);
    expect(router.navigate).toHaveBeenCalledWith([base, 1, 'rules'], {replaceUrl: true});
  });

  it('shows the empty state when nothing is enabled or the call fails', () => {
    getProperties.mockReturnValue(of({success: false, message: 'x'}));
    create(base);
    expect(component.properties).toEqual([]);
    expect(router.navigate).not.toHaveBeenCalled();
  });

  it('shows only the empty state when the URL names a property but none is enabled', () => {
    getProperties.mockReturnValue(of({success: true, model: [{propertyId: 2, name: 'Ejendom Syd', enabled: false}]}));
    create(`${base}/2/rules`);
    expect(component.propertyId).toBeNull();
    expect(router.navigate).not.toHaveBeenCalled();
  });
});
