import { NgModule } from '@angular/core';
import { RouterModule, Routes } from '@angular/router';
import { AreaRulesContainerComponent } from './components';

export const routes: Routes = [
  {
    path: ':propertyAreaId/:propertyId',
    component: AreaRulesContainerComponent,
  },
];

@NgModule({
  imports: [RouterModule.forChild(routes)],
  exports: [RouterModule],
})
export class AreaRulesRouting {}
