import {Component} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {ActivatedRoute, ActivatedRouteSnapshot, RouterOutlet, provideRouter} from '@angular/router';
import {RouterTestingHarness} from '@angular/router/testing';
import {firstValueFrom, of} from 'rxjs';
import {propertyIdParam} from './shared/tail-bite-route';
import {routes} from './tail-bite.routing';

describe('TailBiteRouting', () => {
  const propertyRoute = () => routes[0].children!.find((r) => r.path === ':propertyId')!;

  it('puts every page under the property id, opening the outbreaks by default', () => {
    expect(propertyRoute().component).toBeUndefined();
    expect(propertyRoute().children!.map((r) => r.path))
      .toEqual(['', 'outbreaks', 'outbreaks/:id', 'locations', 'rules', 'action-types']);
    expect(propertyRoute().children![0].redirectTo).toBe('outbreaks');
  });

  it('loads each page on demand', async () => {
    for (const page of propertyRoute().children!.filter((r) => r.path !== '')) {
      expect(page.loadComponent).toBeDefined();
      expect(await (page.loadComponent as () => Promise<unknown>)()).toBeDefined();
    }
  });

  it('lets a page resolve the property id of the componentless :propertyId route', async () => {
    @Component({template: '<router-outlet></router-outlet>', imports: [RouterOutlet]})
    class HostStub {}
    // Same nesting as the real table (shell > :propertyId > page), with the shell swapped for a stub.
    TestBed.configureTestingModule({
      providers: [provideRouter([{path: 'tail-bite', children: [{path: '', component: HostStub, children: routes[0].children}]}])],
    });
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/tail-bite/7/rules');
    let leaf = TestBed.inject(ActivatedRoute).snapshot as ActivatedRouteSnapshot;
    while (leaf.firstChild) {
      leaf = leaf.firstChild;
    }
    expect(leaf.routeConfig!.path).toBe('rules');
    const route = {paramMap: of(leaf.paramMap)} as unknown as ActivatedRoute;
    expect(await firstValueFrom(propertyIdParam(route))).toBe(7);
  });
});
