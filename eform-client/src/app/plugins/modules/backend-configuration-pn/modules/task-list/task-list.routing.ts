import {NgModule} from '@angular/core';
import {RouterModule, Routes} from '@angular/router';
import {TaskListPageComponent} from './components';

const routes: Routes = [{path: '', component: TaskListPageComponent}];

@NgModule({
  imports: [RouterModule.forChild(routes)],
  exports: [RouterModule],
})
export class TaskListRouting {}
