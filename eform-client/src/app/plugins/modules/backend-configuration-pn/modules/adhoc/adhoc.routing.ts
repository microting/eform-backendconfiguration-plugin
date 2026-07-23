import {NgModule} from '@angular/core';
import {RouterModule, Routes} from '@angular/router';
import {AdhocContainerComponent, AdhocHistoryComponent} from './components';

// The Overblik view (toolbar filters + table, F5/F6) is embedded directly
// in AdhocContainerComponent's own template rather than as a routed child
// (see its class-doc comment) - only Historik (F8) is an actual nested
// route, rendered through the container's <router-outlet>.
export const routes: Routes = [
  {
    path: '',
    component: AdhocContainerComponent,
    children: [
      {path: 'history', component: AdhocHistoryComponent},
    ],
  },
];

@NgModule({
  imports: [RouterModule.forChild(routes)],
  exports: [RouterModule],
})
export class AdhocRouting {}
