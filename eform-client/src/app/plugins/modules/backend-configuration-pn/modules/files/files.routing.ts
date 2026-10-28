import {RouterModule, Routes} from '@angular/router';
import {
  FilesContainerComponent,
  FileCreateComponent,
  InboxContainerComponent,
  InboxSettingsComponent,
} from './components';
import {NgModule} from '@angular/core';
import {CanManageGuard} from './guards/can-manage.guard';

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
    canActivate: [CanManageGuard],
  },
  {
    path: 'settings',
    component: InboxSettingsComponent,
    canActivate: [CanManageGuard],
  },
];

@NgModule({
  imports: [RouterModule.forChild(routes)],
  exports: [RouterModule],
})
export class FilesRouting {}
