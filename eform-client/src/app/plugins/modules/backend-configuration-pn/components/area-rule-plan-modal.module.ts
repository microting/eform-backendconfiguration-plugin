import {NgModule} from '@angular/core';
import {CommonModule} from '@angular/common';
import {FormsModule} from '@angular/forms';
import {NgSelectModule} from '@ng-select/ng-select';
import {TranslateModule} from '@ngx-translate/core';
import {MDBBootstrapModule} from 'angular-bootstrap-md';
import {FontAwesomeModule} from '@fortawesome/angular-fontawesome';
import {OWL_DATE_TIME_FORMATS, OwlDateTimeModule} from '@danielmoncada/angular-datetime-picker';
import {MY_MOMENT_FORMATS_FOR_AREA_RULES_PLAN} from '../consts';
import {
  AreaRulePlanModalComponent,
  AreaRuleEntityListModalComponent,
} from './';
import {EformSharedModule} from 'src/app/common/modules/eform-shared/eform-shared.module';
import {MatDialogModule} from '@angular/material/dialog';
import {MatButtonModule} from '@angular/material/button';
import {MatIconModule} from '@angular/material/icon';

@NgModule({
  imports: [
    OwlDateTimeModule,
    CommonModule,
    FormsModule,
    TranslateModule,
    NgSelectModule,
    MDBBootstrapModule,
    FontAwesomeModule,
    EformSharedModule,
    MatDialogModule,
    MatButtonModule,
    MatIconModule,
  ],
  declarations: [
    AreaRulePlanModalComponent,
    AreaRuleEntityListModalComponent,
  ],
  exports: [
    AreaRulePlanModalComponent,
    AreaRuleEntityListModalComponent,
  ],
  providers: [
    {
      provide: OWL_DATE_TIME_FORMATS,
      useValue: MY_MOMENT_FORMATS_FOR_AREA_RULES_PLAN,
    },
  ],
})
export class AreaRulePlanModalModule {
}
