import {RouterModule, Routes} from '@angular/router';
import {
  FilesContainerComponent,
  FileCreateComponent,
} from './components';
import {NgModule} from '@angular/core';

export const routes: Routes = [
  {
    path: '',
    component: FilesContainerComponent,
  },
  {
    path: 'create',
    component: FileCreateComponent,
  },
];

@NgModule({
  imports: [RouterModule.forChild(routes)],
  exports: [RouterModule],
})
export class FilesRouting {}
