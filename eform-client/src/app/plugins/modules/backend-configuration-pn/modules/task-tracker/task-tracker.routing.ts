import { NgModule } from '@angular/core';
import { RouterModule, Routes } from '@angular/router';
import { TaskTrackerContainerComponent } from './components';

export const routes: Routes = [
  {
    path: '',
    component: TaskTrackerContainerComponent,
  },
];

@NgModule({
  imports: [RouterModule.forChild(routes)],
  exports: [RouterModule],
})
export class TaskTrackerRouting {}
