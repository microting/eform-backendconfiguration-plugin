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
import {ComplianceCaseRoutingModule} from './compliance-case-routing.module';
import {CasesModule} from 'src/app/modules';
import {EformImportedModule} from 'src/app/common/modules/eform-imported/eform-imported.module';
import {EformSharedModule} from 'src/app/common/modules/eform-shared/eform-shared.module';
import {EformCasesModule} from 'src/app/common/modules/eform-cases/eform-cases.module';
import {
  ComplianceCaseHeaderComponent
} from 'src/app/plugins/modules/backend-configuration-pn/modules/compliance/components/compliance-case/compliance-case-header/compliance-case-header.component';
import {
  ComplianceCasePageComponent
} from 'src/app/plugins/modules/backend-configuration-pn/modules/compliance/components/compliance-case/compliance-case-page/compliance-case-page.component';

@NgModule({
  declarations: [
    ComplianceCaseHeaderComponent,
    ComplianceCasePageComponent
  ],
  imports: [
    TranslateModule,
    MDBBootstrapModule,
    EformSharedModule,
    ComplianceCaseRoutingModule,
    CommonModule,
    NgSelectModule,
    EformImportedModule,
    GallerizeModule,
    LightboxModule,
    GalleryModule,
    FormsModule,
    FontAwesomeModule,
    CasesModule,
    EformCasesModule
  ]
})
export class ComplianceCaseModule {
}
