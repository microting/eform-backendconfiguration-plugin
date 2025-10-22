import {CommonModule} from '@angular/common';
import {NgModule} from '@angular/core';
import {FormsModule, ReactiveFormsModule} from '@angular/forms';
import {RouterModule} from '@angular/router';
import {NgSelectModule} from '@ng-select/ng-select';
import {TranslateModule} from '@ngx-translate/core';
import {FileUploadModule} from 'ng2-file-upload';
import {EformCasesModule} from 'src/app/common/modules/eform-cases/eform-cases.module';
import {EformSharedTagsModule} from 'src/app/common/modules/eform-shared-tags/eform-shared-tags.module';
import {EformSharedModule} from 'src/app/common/modules/eform-shared/eform-shared.module';
import {BackendConfigurationPnRouting} from './backend-configuration-pn.routing';
import {
  PropertiesContainerComponent,
  PropertyCreateModalComponent,
  PropertyDeleteModalComponent,
  PropertyEditModalComponent,
  PropertiesTableComponent,
  PropertyAreasEditModalComponent,
  PropertyAreasComponent,
  PropertyDocxReportModalComponent,
  ReportContainerComponent,
  ReportHeaderComponent,
  ReportTableComponent,
  CaseDeleteComponent,
} from './components';
import {BackendConfigurationPnLayoutComponent} from './layouts';
import {
  BackendConfigurationPnChemicalsService,
  BackendConfigurationPnPropertiesService,
  BackendConfigurationPnSettingsService,
  BackendConfigurationPnTaskTrackerService,
} from './services';
import {AreaRulePlanModalModule} from './components/area-rule-plan-modal.module';
import {MatCardModule} from '@angular/material/card';
import {MatButtonModule} from '@angular/material/button';
import {MatTooltipModule} from '@angular/material/tooltip';
import {MatIconModule} from '@angular/material/icon';
import {MtxGridModule} from '@ng-matero/extensions/grid';
import {MatInputModule} from '@angular/material/input';
import {MatSortModule} from '@angular/material/sort';
import {MatDialogModule} from '@angular/material/dialog';
import {MtxSelectModule} from '@ng-matero/extensions/select';
import {MatSlideToggleModule} from '@angular/material/slide-toggle';
import {MatExpansionModule} from '@angular/material/expansion';
import {MatCheckboxModule} from '@angular/material/checkbox';
import {ItemsPlanningPnTagsService} from 'src/app/plugins/modules/items-planning-pn/services';
import {MatDatepickerModule} from '@angular/material/datepicker';
import {StoreModule} from '@ngrx/store';
import {
  areaRulesReducer,
  documentsReducer,
  filesReducer,
  propertiesReducer,
  propertyWorkersReducer,
  reportV1Reducer,
  reportV2Reducer,
  statisticsReducer,
  taskManagementReducer,
  taskTrackerReducer,
  taskWizardReducer,
  taskWorkerAssignmentReducer,
} from './state';
import {MatMenu, MatMenuItem, MatMenuTrigger} from "@angular/material/menu";


@NgModule({
  imports: [
    CommonModule,
    TranslateModule,
    FormsModule,
    NgSelectModule,
    EformSharedModule,
    RouterModule,
    BackendConfigurationPnRouting,
    ReactiveFormsModule,
    FileUploadModule,
    EformCasesModule,
    EformSharedTagsModule,
    AreaRulePlanModalModule,
    MatCardModule,
    MatButtonModule,
    MatTooltipModule,
    MatIconModule,
    MtxGridModule,
    MatInputModule,
    MatSortModule,
    MatDialogModule,
    MtxSelectModule,
    MatSlideToggleModule,
    MatExpansionModule,
    MatCheckboxModule,
    MatDatepickerModule,
    StoreModule.forFeature('backendConfigurationPn', {
      areaRulesState: areaRulesReducer,
      documentsState: documentsReducer,
      filesState: filesReducer,
      propertiesState: propertiesReducer,
      propertyWorkersState: propertyWorkersReducer,
      reportsV1State: reportV1Reducer,
      reportsV2State: reportV2Reducer,
      statisticsState: statisticsReducer,
      taskManagementState: taskManagementReducer,
      taskTrackerState: taskTrackerReducer,
      taskWizardState: taskWizardReducer,
      taskWorkerAssignmentState: taskWorkerAssignmentReducer,
    },),
    MatMenu,
    MatMenuTrigger,
    MatMenuItem,
  ],
  declarations: [
    BackendConfigurationPnLayoutComponent,
    PropertiesContainerComponent,
    PropertyCreateModalComponent,
    PropertyEditModalComponent,
    PropertyDeleteModalComponent,
    PropertyDocxReportModalComponent,
    PropertiesTableComponent,
    PropertyAreasEditModalComponent,
    PropertyAreasComponent,
    ReportContainerComponent,
    ReportHeaderComponent,
    ReportTableComponent,
    CaseDeleteComponent
  ],
  providers: [
    BackendConfigurationPnSettingsService,
    BackendConfigurationPnPropertiesService,
    BackendConfigurationPnChemicalsService,
    BackendConfigurationPnTaskTrackerService,
    ItemsPlanningPnTagsService,
  ],
})
export class BackendConfigurationPnModule {
}
