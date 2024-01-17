import {NgModule} from '@angular/core';
import {CommonModule} from '@angular/common';
import {FormsModule} from '@angular/forms';
import {TranslateModule} from '@ngx-translate/core';
import {
  AreaRulePlanModalComponent,
  AreaRuleEntityListModalComponent,
} from './';
import {EformSharedModule} from 'src/app/common/modules/eform-shared/eform-shared.module';
import {MatDialogModule} from '@angular/material/dialog';
import {MatButtonModule} from '@angular/material/button';
import {MatIconModule} from '@angular/material/icon';
import {MatInputModule} from '@angular/material/input';
import {MatSlideToggleModule} from '@angular/material/slide-toggle';
import {MtxSelectModule} from '@ng-matero/extensions/select';
import {MtxGridModule} from '@ng-matero/extensions/grid';
import {MatCheckboxModule} from '@angular/material/checkbox';
import {MatCardModule} from '@angular/material/card';
import {MatDatepickerModule} from '@angular/material/datepicker';

@NgModule({
  imports: [
    CommonModule,
    FormsModule,
    TranslateModule,
    EformSharedModule,
    MatDialogModule,
    MatButtonModule,
    MatIconModule,
    MatInputModule,
    MatSlideToggleModule,
    MtxSelectModule,
    MtxGridModule,
    MatCheckboxModule,
    MatCardModule,
    MatDatepickerModule,
  ],
  declarations: [
    AreaRulePlanModalComponent,
    AreaRuleEntityListModalComponent,
  ],
  exports: [
    AreaRulePlanModalComponent,
    AreaRuleEntityListModalComponent,
  ],
  providers: [],
})
export class AreaRulePlanModalModule {
}
