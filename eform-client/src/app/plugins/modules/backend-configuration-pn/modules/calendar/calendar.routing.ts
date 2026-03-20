import {NgModule} from '@angular/core';
import {RouterModule, Routes} from '@angular/router';
import {CalendarContainerComponent} from './components';

const routes: Routes = [
  {
    path: '',
    component: CalendarContainerComponent,
  },
];

@NgModule({
  imports: [RouterModule.forChild(routes)],
  exports: [RouterModule],
})
export class CalendarRouting {}
