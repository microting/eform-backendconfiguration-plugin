import {NgModule} from '@angular/core';
import {CommonModule} from '@angular/common';
import {TranslateModule} from '@ngx-translate/core';
import {MDBBootstrapModule} from 'angular-bootstrap-md';
import {NgSelectModule} from '@ng-select/ng-select';
import {GallerizeModule} from '@ngx-gallery/gallerize';
import {LightboxModule} from '@ngx-gallery/lightbox';
import {GalleryModule} from '@ngx-gallery/core';
import {FormsModule} from '@angular/forms';
import {FontAwesomeModule} from '@fortawesome/angular-fontawesome';
import {CasesModule} from 'src/app/modules';
import {EformImportedModule} from 'src/app/common/modules/eform-imported/eform-imported.module';
import {EformSharedModule} from 'src/app/common/modules/eform-shared/eform-shared.module';
import {EformCasesModule} from 'src/app/common/modules/eform-cases/eform-cases.module';
import {OwlDateTimeModule} from '@danielmoncada/angular-datetime-picker';
import {
  BackendConfigurationCaseRoutingModule
} from 'src/app/plugins/modules/backend-configuration-pn/components/backend-configuration-case/backend-configuration-case-routing.module';
import {
  BackendConfigurationCasePageComponent
} from 'src/app/plugins/modules/backend-configuration-pn/components/backend-configuration-case/backend-configuration-case-page/backend-configuration-case-page.component';
import {
  BackendConfigurationCaseHeaderComponent
} from 'src/app/plugins/modules/backend-configuration-pn/components/backend-configuration-case/backend-configuration-case-header/backend-configuration-case-header.component';

@NgModule({
  declarations: [
    BackendConfigurationCaseHeaderComponent,
    BackendConfigurationCasePageComponent
  ],
  imports: [
    TranslateModule,
    MDBBootstrapModule,
    EformSharedModule,
    BackendConfigurationCaseRoutingModule,
    CommonModule,
    NgSelectModule,
    EformImportedModule,
    GallerizeModule,
    LightboxModule,
    GalleryModule,
    FormsModule,
    FontAwesomeModule,
    CasesModule,
    EformCasesModule,
    OwlDateTimeModule,
  ]
})
export class BackendConfigurationCaseModule {
}
