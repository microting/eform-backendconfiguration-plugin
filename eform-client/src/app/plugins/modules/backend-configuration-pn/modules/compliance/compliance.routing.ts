import { NgModule } from '@angular/core';
import { RouterModule, Routes } from '@angular/router';
import { CompliancesContainerComponent } from './components';

export const routes: Routes = [
  {
    path: ':propertyId',
    component: CompliancesContainerComponent,
  },
];

@NgModule({
  imports: [RouterModule.forChild(routes)],
  exports: [RouterModule],
})
export class CompliancesRouting {}
