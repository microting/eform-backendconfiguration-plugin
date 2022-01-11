import { NgModule } from '@angular/core';
import { RouterModule, Routes } from '@angular/router';
import { CompliancesContainerComponent } from './components';
import {AreaRulesContainerComponent} from 'src/app/plugins/modules/backend-configuration-pn/modules/area-rules/components';

export const routes: Routes = [
  {
    path: ':propertyId',
    component: CompliancesContainerComponent,
  },
  {
    path: ':propertyId/:complianceStatusThirty',
    component: CompliancesContainerComponent,
  },
];

@NgModule({
  imports: [RouterModule.forChild(routes)],
  exports: [RouterModule],
})
export class CompliancesRouting {}
