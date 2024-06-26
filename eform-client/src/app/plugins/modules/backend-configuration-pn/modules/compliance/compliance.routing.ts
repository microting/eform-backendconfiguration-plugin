import {NgModule} from '@angular/core';
import {RouterModule, Routes} from '@angular/router';
import {CompliancesContainerComponent} from './components';

export const routes: Routes = [
  {
    path: ':propertyId',
    component: CompliancesContainerComponent,
  },
  {
    path: ':propertyId/:complianceStatusThirty',
    component: CompliancesContainerComponent,
  },
  {
    path: 'case',
    loadChildren: () =>
      import('./components/compliance-case/compliance-case.module').then(
        (m) => m.ComplianceCaseModule
      ),
  },
];

@NgModule({
  imports: [RouterModule.forChild(routes)],
  exports: [RouterModule],
})
export class CompliancesRouting {
}
