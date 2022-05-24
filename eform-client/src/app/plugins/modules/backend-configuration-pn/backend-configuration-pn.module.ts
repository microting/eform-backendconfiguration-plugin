import { CommonModule } from '@angular/common';
import { NgModule } from '@angular/core';
import { FormsModule, ReactiveFormsModule } from '@angular/forms';
import { RouterModule } from '@angular/router';
import { OwlDateTimeModule, /*OWL_DATE_TIME_FORMATS*/ } from '@danielmoncada/angular-datetime-picker';
import { FontAwesomeModule } from '@fortawesome/angular-fontawesome';
import { NgSelectModule } from '@ng-select/ng-select';
import { TranslateModule } from '@ngx-translate/core';
import { MDBBootstrapModule } from 'angular-bootstrap-md';
import { FileUploadModule } from 'ng2-file-upload';
import { EformCasesModule } from 'src/app/common/modules/eform-cases/eform-cases.module';
import { EformSharedTagsModule } from 'src/app/common/modules/eform-shared-tags/eform-shared-tags.module';
import { EformSharedModule } from 'src/app/common/modules/eform-shared/eform-shared.module';
import { BackendConfigurationPnRouting } from './backend-configuration-pn.routing.module';
import {
  BackendConfigurationSettingsComponent,
  PropertiesContainerComponent,
  PropertyCreateModalComponent,
  PropertyDeleteModalComponent,
  PropertyEditModalComponent,
  PropertiesTableComponent,
  PropertyAreasEditModalComponent,
  PropertyAreasComponent,
  PropertyDocxReportModalComponent,
} from './components';
import { BackendConfigurationPnLayoutComponent } from './layouts';
import {
  BackendConfigurationPnPropertiesService,
  BackendConfigurationPnSettingsService,
} from './services';
import { backendConfigurationStoreProviders } from './store-providers.config';
import {AreaRulePlanModalModule} from 'src/app/plugins/modules/backend-configuration-pn/components/area-rule-plan-modal.module';
// import {MY_MOMENT_FORMATS_FOR_BACKEND_CONFIGURATIONS_PLUGIN} from './consts/custom-date-time-adapter';

@NgModule({
    imports: [
        CommonModule,
        MDBBootstrapModule,
        TranslateModule,
        FormsModule,
        NgSelectModule,
        EformSharedModule,
        FontAwesomeModule,
        RouterModule,
        BackendConfigurationPnRouting,
        ReactiveFormsModule,
        FileUploadModule,
        OwlDateTimeModule,
        EformCasesModule,
        EformSharedTagsModule,
        AreaRulePlanModalModule,
    ],
  declarations: [
    BackendConfigurationPnLayoutComponent,
    BackendConfigurationSettingsComponent,
    PropertiesContainerComponent,
    PropertyCreateModalComponent,
    PropertyEditModalComponent,
    PropertyDeleteModalComponent,
    PropertyDocxReportModalComponent,
    PropertiesTableComponent,
    PropertyAreasEditModalComponent,
    PropertyAreasComponent,
  ],
  providers: [
    BackendConfigurationPnSettingsService,
    BackendConfigurationPnPropertiesService,
    ...backendConfigurationStoreProviders,
    // { provide: OWL_DATE_TIME_FORMATS, useValue: MY_MOMENT_FORMATS_FOR_BACKEND_CONFIGURATIONS_PLUGIN },
  ],
})
export class BackendConfigurationPnModule {}
