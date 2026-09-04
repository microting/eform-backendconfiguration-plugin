import { NgModule } from '@angular/core';
import { RouterModule, Routes } from '@angular/router';
import { AuthGuard, IsAdminGuard, PermissionGuard } from 'src/app/common/guards';
import {
  GoogleDriveAccountsComponent,
  GoogleDriveOAuthFinishComponent,
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
        // Intentionally open to every user of this plugin (spec
        // 2026-08-24-adhoc-tasks-unrestricted-access-design). The
        // adhoc_enable claim was never granted to non-admin roles and is
        // enforced nowhere server-side, so the guard only ever cancelled
        // navigation for the users who were supposed to use the page.
        // The parent route still requires backend_configuration_plugin_access.
        // AuthGuard is deliberate — it is the explicit "open to any logged-in
        // user" pattern also used by calendar, compliances and
        // property-workers. Do not re-add PermissionGuard or a
        // requiredPermission here.
        path: 'adhoc-tasks',
        canActivate: [AuthGuard],
        loadChildren: () =>
          import('./modules/adhoc/adhoc.module').then(
            (m) => m.AdhocModule
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
        path: 'reportsv2',
        canActivate: [AuthGuard],
        loadChildren: () =>
          import('./modules/reports/reports.module').then(
            (m) => m.ReportsModule
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
      {
        path: 'statistics',
        /*canActivate: [PermissionGuard],*/
        loadChildren: () =>
          import('./modules/statistics/statistics.module').then(
            (m) => m.StatisticsModule
          ),
      },
      {
        path: 'calendar',
        canActivate: [AuthGuard],
        loadChildren: () =>
          import('./modules/calendar/calendar.module').then(
            (m) => m.CalendarModule
          ),
      },
      {
        path: 'calendar-task-list',
        canActivate: [AuthGuard],
        loadChildren: () =>
          import('./modules/calendar-task-list/calendar-task-list.module').then(
            (m) => m.CalendarTaskListModule
          ),
      },
      {
        // Standalone Compliance page (#1160/#1163).
        //
        // Deliberately a sibling of 'compliances', not a child: that module's
        // routing table (modules/compliance/compliance.routing.ts) declares
        // ':propertyId' FIRST, and Angular matches in declaration order, so any
        // new single-segment literal under 'compliances' would be swallowed and
        // rendered as CompliancesContainerComponent with a garbage propertyId.
        // 'compliances/case/...' only survives because it is always seven
        // segments deep in practice. Reordering that table would be the clean
        // fix, but it is load-bearing for task-tracker and is out of scope here.
        //
        // Deliberately AuthGuard, not PermissionGuard/IsAdminGuard: decision 6
        // in #1160 makes this page available to every authenticated user of the
        // plugin. The parent route already enforces
        // backend_configuration_plugin_access, which is the whole boundary.
        // Same explicit "open to any logged-in plugin user" marker as
        // adhoc-tasks above. Do not add a requiredPermission here.
        path: 'compliance-report',
        canActivate: [AuthGuard],
        loadChildren: () =>
          import('./modules/compliance-report/compliance-report.module').then(
            (m) => m.ComplianceReportModule
          ),
      },
      {
        path: 'task-list',
        canActivate: [IsAdminGuard],
        loadChildren: () =>
          import('./modules/task-list/task-list.module').then(
            (m) => m.TaskListModule
          ),
      },
      {
        // Google OAuth popup landing route. The backend's
        // GoogleDriveController.OAuthFinish redirects here with either
        // ?gdrive_success=true or ?gdrive_err=<reason>. The component
        // posts a `gd_oauth_done` message to window.opener (the calendar
        // attach-file modal) and then closes itself.
        path: 'google-drive-oauth-finish',
        canActivate: [AuthGuard],
        component: GoogleDriveOAuthFinishComponent,
      },
      {
        // PR-8 settings: connected-accounts panel. Standalone top-level
        // route — the plugin doesn't have an existing settings page to
        // splice into, and a deep-linkable URL keeps the disconnect flow
        // back-button-friendly.
        path: 'google-drive-accounts',
        canActivate: [AuthGuard],
        component: GoogleDriveAccountsComponent,
      },
    ],
  },
];

@NgModule({
  imports: [RouterModule.forChild(routes)],
  exports: [RouterModule],
})
export class BackendConfigurationPnRouting {}
