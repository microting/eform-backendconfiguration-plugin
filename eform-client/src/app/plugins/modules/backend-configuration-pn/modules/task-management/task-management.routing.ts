import { NgModule } from '@angular/core';
import { RouterModule, Routes } from '@angular/router';
import { TaskManagementContainerComponent } from './components';

export const routes: Routes = [
  {
    path: '',
    component: TaskManagementContainerComponent,
  },
];

@NgModule({
  imports: [RouterModule.forChild(routes)],
  exports: [RouterModule],
})
export class TaskManagementRouting {}
