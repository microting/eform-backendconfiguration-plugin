import {RouterModule, Routes} from '@angular/router';
import {
  FilesContainerComponent
} from './components';
import {NgModule} from '@angular/core';

export const routes: Routes = [
  {
    path: '',
    component: FilesContainerComponent,
  },
];

@NgModule({
  imports: [RouterModule.forChild(routes)],
  exports: [RouterModule],
})
export class FilesRouting {}
