import { CommonModule } from '@angular/common';
import { NgModule } from '@angular/core';
import { FormsModule, ReactiveFormsModule } from '@angular/forms';
import { RouterModule } from '@angular/router';
import {
  OwlDateTimeModule,
  OwlMomentDateTimeModule,
  OWL_DATE_TIME_FORMATS,
} from '@danielmoncada/angular-datetime-picker';
import { FontAwesomeModule } from '@fortawesome/angular-fontawesome';
import { NgSelectModule } from '@ng-select/ng-select';
import { TranslateModule } from '@ngx-translate/core';
import { MDBBootstrapModule } from 'angular-bootstrap-md';
import { FileUploadModule } from 'ng2-file-upload';
import { EformSharedModule } from 'src/app/common/modules/eform-shared/eform-shared.module';
import {
  AreaRuleCreateModalComponent,
  AreaRuleDeleteModalComponent,
  AreaRuleEditModalComponent,
  AreaRulePlanModalComponent,
  AreaRulesContainerComponent,
  AreaRulesTableComponent,
} from './components';
import { AreaRulesRouting } from './area-rules.routing';
import { MY_MOMENT_FORMATS } from '../../consts/custom-date-time-adapter';
import { UiSwitchModule } from 'ngx-ui-switch';

@NgModule({
  declarations: [
    AreaRulesContainerComponent,
    AreaRulesTableComponent,
    AreaRuleCreateModalComponent,
    AreaRuleEditModalComponent,
    AreaRuleDeleteModalComponent,
    AreaRulePlanModalComponent,
  ],
  imports: [
    CommonModule,
    MDBBootstrapModule,
    TranslateModule,
    FormsModule,
    NgSelectModule,
    EformSharedModule,
    FontAwesomeModule,
    RouterModule,
    ReactiveFormsModule,
    FileUploadModule,
    OwlDateTimeModule,
    AreaRulesRouting,
    OwlDateTimeModule,
    OwlMomentDateTimeModule,
    UiSwitchModule,
  ],
  providers: [{ provide: OWL_DATE_TIME_FORMATS, useValue: MY_MOMENT_FORMATS }],
})
export class AreaRulesModule {}
