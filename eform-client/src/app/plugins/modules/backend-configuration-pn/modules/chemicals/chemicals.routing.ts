import {NgModule} from '@angular/core';
import {Route, RouterModule, Routes} from '@angular/router';
import {CHEMICAL_SCREENS} from './chemicals-screens';
import {chemicalUnsavedChangesGuard} from './chemicals-unsaved-changes';
import {ChemicalsContainerComponent} from './components';

const screenRoutes: Route[] = CHEMICAL_SCREENS.map((screen) => ({
  path: screen.path,
  component: screen.component,
  data: {titleKey: screen.titleKey},
  canDeactivate: [chemicalUnsavedChangesGuard],
}));

/**
 * /plugins/backend-configuration-pn/chemicals/... (spec section 11 W1-W6). Each screen
 * is its own menu child and route (no in-page tabs); the container owns the page frame
 * and the property picker. The bare Kemi root (the Plan D digest link) redirects to W1
 * Beholdning; the redirect is relative, so ?propertyId= and ?filter= survive it, and its
 * target has its own path, so it cannot loop (M-3).
 */
export const routes: Routes = [
  {
    path: '',
    component: ChemicalsContainerComponent,
    children: [
      {path: '', pathMatch: 'full', redirectTo: 'inventory'},
      ...screenRoutes,
    ],
  },
];

@NgModule({
  imports: [RouterModule.forChild(routes)],
  exports: [RouterModule],
})
export class ChemicalsRouting {}
