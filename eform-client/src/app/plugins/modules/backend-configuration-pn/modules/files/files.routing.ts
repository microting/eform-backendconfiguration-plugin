import {RouterModule, Routes} from '@angular/router';
import {
  FilesContainerComponent,
  FileCreateComponent,
  InboxContainerComponent,
  InboxSettingsComponent,
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
  {
    path: 'inbox',
    component: InboxContainerComponent,
  },
  {
    path: 'settings',
    component: InboxSettingsComponent,
  },
];

@NgModule({
  imports: [RouterModule.forChild(routes)],
  exports: [RouterModule],
})
export class FilesRouting {}
