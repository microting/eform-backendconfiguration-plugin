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
  AreaRulesContainerComponent,
  AreaRulesTableComponent,
} from './components';
import { AreaRulesRouting } from './area-rules.routing';
import {AreaRulePlanModalModule} from '../../components/area-rule-plan-modal.module';

@NgModule({
    declarations: [
        AreaRulesContainerComponent,
        AreaRulesTableComponent,
        AreaRuleCreateModalComponent,
        AreaRuleEditModalComponent,
        AreaRuleDeleteModalComponent,
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
    AreaRulePlanModalModule,
  ]
})
export class AreaRulesModule {}
