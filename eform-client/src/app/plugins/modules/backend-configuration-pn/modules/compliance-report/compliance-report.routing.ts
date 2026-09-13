import {NgModule} from '@angular/core';
import {RouterModule, Routes} from '@angular/router';
import {ComplianceReportPageComponent} from './components';

// One route. The three view modes are NOT routed children — the mode toggle
// must preserve the fetched result across switches (#1163 §10), and a router
// navigation would tear the child down and take `reportVisible` with it.
export const routes: Routes = [
  {
    path: '',
    component: ComplianceReportPageComponent,
  },
];

@NgModule({
  imports: [RouterModule.forChild(routes)],
  exports: [RouterModule],
})
export class ComplianceReportRouting {}
