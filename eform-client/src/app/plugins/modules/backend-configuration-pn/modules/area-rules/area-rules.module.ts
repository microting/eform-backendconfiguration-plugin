import {CommonModule} from '@angular/common';
import {NgModule} from '@angular/core';
import {FormsModule, ReactiveFormsModule} from '@angular/forms';
import {RouterModule} from '@angular/router';
import {
  OwlDateTimeModule,
  // OwlMomentDateTimeModule,
  OWL_DATE_TIME_FORMATS,
} from '@danielmoncada/angular-datetime-picker';
import {TranslateModule} from '@ngx-translate/core';
import {FileUploadModule} from 'ng2-file-upload';
import {EformSharedModule} from 'src/app/common/modules/eform-shared/eform-shared.module';
import {
  AreaRuleCreateModalComponent,
  AreaRuleDeleteModalComponent,
  AreaRuleEditModalComponent,
  AreaRulesContainerComponent,
  AreaRulesTableComponent,
} from './components';
import {AreaRulesRouting} from './area-rules.routing';
import {AreaRulePlanModalModule} from '../../components/area-rule-plan-modal.module';
import {MatTooltipModule} from '@angular/material/tooltip';
import {MatButtonModule} from '@angular/material/button';
import {MatIconModule} from '@angular/material/icon';
import {MtxGridModule} from '@ng-matero/extensions/grid';
import {MatDialogModule} from '@angular/material/dialog';
import {MatFormFieldModule} from '@angular/material/form-field';
import {MtxSelectModule} from '@ng-matero/extensions/select';
import {MatInputModule} from '@angular/material/input';
import {MatCheckboxModule} from '@angular/material/checkbox';
import {MatCardModule} from '@angular/material/card';
import {MatTableModule} from '@angular/material/table';

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
    TranslateModule,
    FormsModule,
    EformSharedModule,
    RouterModule,
    ReactiveFormsModule,
    FileUploadModule,
    OwlDateTimeModule,
    AreaRulesRouting,
    OwlDateTimeModule,
    // OwlMomentDateTimeModule,
    AreaRulePlanModalModule,
    MatTooltipModule,
    MatButtonModule,
    MatIconModule,
    MtxGridModule,
    MatDialogModule,
    MatFormFieldModule,
    MtxSelectModule,
    MatInputModule,
    MatCheckboxModule,
    MatCardModule,
    MatTableModule,
  ]
})
export class AreaRulesModule {
}
