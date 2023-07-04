import { NgModule } from '@angular/core';
import { RouterModule, Routes } from '@angular/router';
import { AuthGuard, PermissionGuard } from 'src/app/common/guards';
import {
  PropertiesContainerComponent,
  PropertyAreasComponent, ReportContainerComponent,
} from './components';
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
        canActivate: [PermissionGuard],
        data: {
          requiredPermission: BackendConfigurationPnClaims.enableTaskManagement,
        },
        loadChildren: () =>
          import('./modules/task-management/task-management.module').then(
            (m) => m.TaskManagementModule
          ),
      },
      {
        path: 'task-tracker',
        /*canActivate: [PermissionGuard],*/
        loadChildren: () =>
          import('./modules/task-tracker/task-tracker.module').then(
            (m) => m.TaskTrackerModule
          ),
      },
      {
        path: 'documents',canActivate: [PermissionGuard],
        data: {
          requiredPermission: BackendConfigurationPnClaims.enableDocumentManagement,
        },
        loadChildren: () =>
          import('./modules/documents/documents.module').then(
            (m) => m.DocumentsModule
          ),
      },
      {
        path: 'reports',
        canActivate: [AuthGuard],
        component: ReportContainerComponent,
      },
      {
        path: 'reports/:dateFrom/:dateTo',
        canActivate: [AuthGuard],
        component: ReportContainerComponent,
      },
      {
        path: 'case',
        loadChildren: () =>
          import('./components/backend-configuration-case/backend-configuration-case.module').then(
            (m) => m.BackendConfigurationCaseModule
          ),
      },
      {
        path: 'files',
/*        canActivate: [PermissionGuard],
        data: {
          requiredPermission: BackendConfigurationPnClaims.enableFilesManagement,
        },*/
        loadChildren: () =>
          import('./modules/files/files.module').then(
            (m) => m.FilesModule
          ),
      },
      {
        path: 'task-wizard',
        /*canActivate: [PermissionGuard],*/
        loadChildren: () =>
          import('./modules/task-wizard/task-wizard.module').then(
            (m) => m.TaskWizardModule
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
