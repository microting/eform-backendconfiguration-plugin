import {NgModule} from '@angular/core';
import {CommonModule} from '@angular/common';
import {TranslateModule} from '@ngx-translate/core';
import {FormsModule} from '@angular/forms';
import {ComplianceCaseRouting} from './compliance-case.routing';
import {CasesModule} from 'src/app/modules';
import {EformImportedModule} from 'src/app/common/modules/eform-imported/eform-imported.module';
import {EformSharedModule} from 'src/app/common/modules/eform-shared/eform-shared.module';
import {EformCasesModule} from 'src/app/common/modules/eform-cases/eform-cases.module';
import {
  ComplianceCaseHeaderComponent
} from '../compliance-case/compliance-case-header/compliance-case-header.component';
import {
  ComplianceCasePageComponent
} from '../compliance-case/compliance-case-page/compliance-case-page.component';
import {MatButtonModule} from '@angular/material/button';
import {MatCardModule} from '@angular/material/card';
import {MatFormFieldModule} from '@angular/material/form-field';
import {MatIconModule} from '@angular/material/icon';
import {MatInputModule} from '@angular/material/input';
import {MatDatepickerModule} from '@angular/material/datepicker';

@NgModule({
  declarations: [
    ComplianceCaseHeaderComponent,
    ComplianceCasePageComponent
  ],
  imports: [
    TranslateModule,
    EformSharedModule,
    ComplianceCaseRouting,
    CommonModule,
    EformImportedModule,
    FormsModule,
    CasesModule,
    EformCasesModule,
    MatButtonModule,
    MatCardModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatDatepickerModule
  ]
})
export class ComplianceCaseModule {
}
