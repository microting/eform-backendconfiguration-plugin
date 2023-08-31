import {RouterModule, Routes} from '@angular/router';
import {
  StatisticsContainerComponent,
} from './components';
import {NgModule} from '@angular/core';

export const routes: Routes = [
  {
    path: '',
    component: StatisticsContainerComponent,
  },
];

@NgModule({
  imports: [RouterModule.forChild(routes)],
  exports: [RouterModule],
})
export class StatisticsRouting {}
