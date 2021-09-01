import { NgModule } from '@angular/core';
import { RouterModule, Routes } from '@angular/router';
import { PermissionGuard } from 'src/app/common/guards';
import { PropertiesContainerComponent } from './components';
import { BackendConfigurationPnClaims } from './enums';
import { BackendConfigurationPnLayoutComponent } from './layouts';

export const routes: Routes = [
  {
    path: '',
    component: BackendConfigurationPnLayoutComponent,
    canActivate: [PermissionGuard],
    data: {
      requiredPermission:
        BackendConfigurationPnClaims.accessBackendConfigurationPlugin,
    },
    children: [
      {
        path: 'properties',
        canActivate: [PermissionGuard],
        data: {
          requiredPermission: BackendConfigurationPnClaims.getProperties,
        },
        component: PropertiesContainerComponent,
      },
    ],
  },
];

@NgModule({
  imports: [RouterModule.forChild(routes)],
  exports: [RouterModule],
})
export class BackendConfigurationPnRouting {}
