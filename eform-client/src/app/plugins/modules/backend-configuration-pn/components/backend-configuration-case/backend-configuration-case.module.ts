import {NgModule} from '@angular/core';
import {CommonModule} from '@angular/common';
import {TranslateModule} from '@ngx-translate/core';
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
} from './backend-configuration-case-routing.module';
import {
  BackendConfigurationCasePageComponent
} from './backend-configuration-case-page/backend-configuration-case-page.component';
// import {
//   BackendConfigurationCaseHeaderComponent
// } from './backend-configuration-case-header/backend-configuration-case-header.component';
import {MatCardModule} from '@angular/material/card';
import {MatFormFieldModule} from '@angular/material/form-field';
import {MatIconModule} from '@angular/material/icon';
import {MatInputModule} from '@angular/material/input';
import {MatButtonModule} from '@angular/material/button';

@NgModule({
  declarations: [
    // BackendConfigurationCaseHeaderComponent,
    BackendConfigurationCasePageComponent
  ],
  imports: [
    TranslateModule,
    EformSharedModule,
    BackendConfigurationCaseRoutingModule,
    CommonModule,
    EformImportedModule,
    GallerizeModule,
    LightboxModule,
    GalleryModule,
    FormsModule,
    FontAwesomeModule,
    CasesModule,
    EformCasesModule,
    OwlDateTimeModule,
    MatCardModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatButtonModule,
  ]
})
export class BackendConfigurationCaseModule {
}
