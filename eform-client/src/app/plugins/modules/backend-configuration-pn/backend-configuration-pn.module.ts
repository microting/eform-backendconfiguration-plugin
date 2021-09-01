import { CommonModule } from '@angular/common';
import { NgModule } from '@angular/core';
import { FormsModule, ReactiveFormsModule } from '@angular/forms';
import { RouterModule } from '@angular/router';
import { OwlDateTimeModule } from '@danielmoncada/angular-datetime-picker';
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
  PlanningsHeaderComponent,
  PlanningsTableComponent,
  PropertiesContainerComponent,
  PropertyCreateComponent,
  PropertyDeleteComponent,
  PropertyEditComponent,
} from './components';
import { BackendConfigurationPnLayoutComponent } from './layouts';
import {
  BackendConfigurationPnPropertiesService,
  BackendConfigurationPnSettingsService,
} from './services';
import { backendConfigurationStoreProviders } from './store-providers.config';

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
  ],
  declarations: [
    BackendConfigurationPnLayoutComponent,
    BackendConfigurationSettingsComponent,
    PropertiesContainerComponent,
    PropertyCreateComponent,
    PropertyEditComponent,
    PropertyDeleteComponent,
    PlanningsHeaderComponent,
    PlanningsTableComponent,
  ],
  providers: [
    BackendConfigurationPnSettingsService,
    BackendConfigurationPnPropertiesService,
    ...backendConfigurationStoreProviders,
  ],
})
export class BackendConfigurationPnModule {}
