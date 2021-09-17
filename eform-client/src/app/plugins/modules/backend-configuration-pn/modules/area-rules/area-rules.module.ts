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
import { EformSharedModule } from 'src/app/common/modules/eform-shared/eform-shared.module';
import { AreaRulesRouting } from './area-rules.routing';
import {
  AreaRuleCreateModalComponent,
  AreaRuleDeleteModalComponent,
  AreaRuleEditModalComponent,
  AreaRulePlanModalComponent,
  AreaRulePlanT1Component,
  AreaRulePlanT2Component,
  AreaRulePlanT3Component,
  AreaRulePlanT4Component,
  AreaRulePlanT5Component,
  AreaRulesContainerComponent,
  AreaRulesTableComponent,
} from './components';

@NgModule({
  declarations: [
    AreaRulesContainerComponent,
    AreaRulesTableComponent,
    AreaRuleCreateModalComponent,
    AreaRuleEditModalComponent,
    AreaRuleDeleteModalComponent,
    AreaRulePlanModalComponent,
    AreaRulePlanT1Component,
    AreaRulePlanT2Component,
    AreaRulePlanT3Component,
    AreaRulePlanT4Component,
    AreaRulePlanT5Component,
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
  ],
})
export class AreaRulesModule {}
