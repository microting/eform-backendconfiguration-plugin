import {NgModule} from '@angular/core';
import {RouterModule, Routes} from '@angular/router';
import {AdhocContainerComponent} from './components';

// Child routes ('' => the table view, 'history' => AdhocHistoryComponent)
// are added by follow-up tasks F5/F8 once those components exist; the
// container's segment toggle already links to them (harmless before
// they're registered - the router just won't match until then).
export const routes: Routes = [
  {
    path: '',
    component: AdhocContainerComponent,
  },
];

@NgModule({
  imports: [RouterModule.forChild(routes)],
  exports: [RouterModule],
})
export class AdhocRouting {}
