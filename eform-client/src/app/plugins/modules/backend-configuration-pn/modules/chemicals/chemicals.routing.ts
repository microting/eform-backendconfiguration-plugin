import {NgModule} from '@angular/core';
import {Route, RouterModule, Routes} from '@angular/router';
import {CHEMICAL_SCREENS} from './chemicals-screens';
import {chemicalUnsavedChangesGuard} from './chemicals-unsaved-changes';
import {ChemicalsContainerComponent} from './components';

const screenRoutes: Route[] = CHEMICAL_SCREENS.map((screen) => ({
  path: screen.path,
  // Only the bare /chemicals opens the root screen (Angular's rule for an empty-path leaf).
  ...(screen.path === '' ? {pathMatch: 'full' as const} : {}),
  component: screen.component,
  data: {titleKey: screen.titleKey},
  canDeactivate: [chemicalUnsavedChangesGuard],
}));

/**
 * /plugins/backend-configuration-pn/chemicals/... (spec section 11 W1-W6). Each screen
 * is its own menu child and route (no in-page tabs); the container owns the page frame
 * and the property picker. W1 Beholdning IS the empty path, so there is no redirect
 * (a '' -> '' redirect would loop, M-3).
 */
export const routes: Routes = [
  {
    path: '',
    component: ChemicalsContainerComponent,
    children: screenRoutes,
  },
];

@NgModule({
  imports: [RouterModule.forChild(routes)],
  exports: [RouterModule],
})
export class ChemicalsRouting {}
