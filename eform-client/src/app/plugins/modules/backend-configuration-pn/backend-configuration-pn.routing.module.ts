import { NgModule } from '@angular/core';
import { RouterModule, Routes } from '@angular/router';
import { AuthGuard, PermissionGuard } from 'src/app/common/guards';
import {
  PropertiesContainerComponent,
  PropertyAreasComponent,
} from './components';
import { BackendConfigurationPnClaims } from './enums';
import { BackendConfigurationPnLayoutComponent } from './layouts';

export const routes: Routes = [
  {
    path: '',
    component: BackendConfigurationPnLayoutComponent,
    // canActivate: [PermissionGuard],
    // data: {
    //   requiredPermission:
    //     BackendConfigurationPnClaims.accessBackendConfigurationPlugin,
    // },
    children: [
      {
        path: 'properties',
        // canActivate: [PermissionGuard],
        // data: {
        //   requiredPermission: BackendConfigurationPnClaims.getProperties,
        // },
        component: PropertiesContainerComponent,
      },
      {
        path: 'property-areas/:propertyId',
        canActivate: [AuthGuard],
        component: PropertyAreasComponent,
      },
      {
        path: 'property-workers',
        canActivate: [AuthGuard],
        loadChildren: () =>
          import('./modules/property-workers/property-workers.module').then(
            (m) => m.PropertyWorkersModule
          ),
      },
      {
        path: 'area-rules',
        canActivate: [AuthGuard],
        loadChildren: () =>
          import('./modules/area-rules/area-rules.module').then(
            (m) => m.AreaRulesModule
          ),
      },
      {
        path: 'compliances',
        canActivate: [AuthGuard],
        loadChildren: () =>
          import('./modules/compliance/compliance.module').then(
            (m) => m.CompliancesModule
          ),
      },
      {
        path: 'task-worker-assignments',
        canActivate: [AuthGuard],
        loadChildren: () =>
          import('./modules/task-worker-assignments/task-worker-assignments.module').then(
            (m) => m.TaskWorkerAssignmentsModule
          ),
      },
      {
        path: 'task-management',
        canActivate: [AuthGuard],
        loadChildren: () =>
          import('./modules/task-management/task-management.module').then(
            (m) => m.TaskManagementModule
          ),
      },
    ],
  },
];

@NgModule({
  imports: [RouterModule.forChild(routes)],
  exports: [RouterModule],
})
export class BackendConfigurationPnRouting {}
