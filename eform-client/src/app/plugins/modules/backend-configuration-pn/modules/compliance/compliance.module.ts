import {CommonModule} from '@angular/common';
import {NgModule} from '@angular/core';
import {RouterModule} from '@angular/router';
import {TranslateModule} from '@ngx-translate/core';
import {
  CompliancesContainerComponent,
  CompliancesTableComponent,
  ComplianceDeleteComponent
} from './components';
import {CompliancesRouting} from './compliance.routing';
import {EformSharedModule} from 'src/app/common/modules/eform-shared/eform-shared.module';
import {MtxGridModule} from '@ng-matero/extensions/grid';
import {MatButtonModule} from '@angular/material/button';
import {MatDialogModule} from '@angular/material/dialog';

@NgModule({
  imports: [
    CommonModule,
    TranslateModule,
    RouterModule,
    CompliancesRouting,
    EformSharedModule,
    MtxGridModule,
    MatButtonModule,
    MatDialogModule,
    CompliancesContainerComponent,
    CompliancesTableComponent,
    ComplianceDeleteComponent,
  ],
  providers: [],
})
export class CompliancesModule {
}
