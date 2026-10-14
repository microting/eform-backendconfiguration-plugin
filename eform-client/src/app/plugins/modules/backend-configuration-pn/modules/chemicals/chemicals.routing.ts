import {NgModule, Type} from '@angular/core';
import {Route, RouterModule, Routes} from '@angular/router';
import {CHEMICAL_SCREENS} from './chemicals-screens';
import {ChemicalPermissionsPageComponent, ChemicalsContainerComponent} from './components';

/** Page component per registered screen path. */
const SCREEN_COMPONENTS: Record<string, Type<unknown>> = {
  permissions: ChemicalPermissionsPageComponent,
};

const screenRoutes: Route[] = CHEMICAL_SCREENS.map((screen) => ({
  path: screen.path,
  component: SCREEN_COMPONENTS[screen.path],
  data: {titleKey: screen.titleKey},
}));

/**
 * /plugins/backend-configuration-pn/chemicals/... (spec section 11 W1-W6). Each screen
 * is its own menu child and route (no in-page tabs); the container owns the page frame
 * and the property picker. The empty path redirects to the first registered screen.
 */
export const routes: Routes = [
  {
    path: '',
    component: ChemicalsContainerComponent,
    children: [
      {path: '', pathMatch: 'full', redirectTo: CHEMICAL_SCREENS[0].path},
      ...screenRoutes,
    ],
  },
];

@NgModule({
  imports: [RouterModule.forChild(routes)],
  exports: [RouterModule],
})
export class ChemicalsRouting {}
