import {RouterModule, Routes} from '@angular/router';
import {
  FilesContainerComponent,
  FileCreateComponent,
  InboxContainerComponent,
  InboxSettingsComponent,
} from './components';
import {NgModule} from '@angular/core';
import {IsAdminGuard} from 'src/app/common/guards';

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
    canActivate: [IsAdminGuard],
  },
  {
    path: 'settings',
    component: InboxSettingsComponent,
    canActivate: [IsAdminGuard],
  },
];

@NgModule({
  imports: [RouterModule.forChild(routes)],
  exports: [RouterModule],
})
export class FilesRouting {}
