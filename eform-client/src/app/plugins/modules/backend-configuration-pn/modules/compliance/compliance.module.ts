import {CommonModule} from '@angular/common';
import {NgModule} from '@angular/core';
import {RouterModule} from '@angular/router';
import {
  OwlDateTimeModule,
  OwlMomentDateTimeModule,
  OWL_DATE_TIME_FORMATS,
} from '@danielmoncada/angular-datetime-picker';
import {TranslateModule} from '@ngx-translate/core';
import {
  CompliancesContainerComponent,
  CompliancesTableComponent,
  ComplianceDeleteComponent
} from './components';
import {CompliancesRouting} from './compliance.routing';
import {MY_MOMENT_FORMATS_FOR_BACKEND_CONFIGURATIONS_PLUGIN} from '../../consts/custom-date-time-adapter';
import {EformSharedModule} from 'src/app/common/modules/eform-shared/eform-shared.module';
import {MtxGridModule} from '@ng-matero/extensions/grid';
import {MatButtonModule} from '@angular/material/button';
import {MatDialogModule} from '@angular/material/dialog';

@NgModule({
  declarations: [CompliancesContainerComponent, CompliancesTableComponent, ComplianceDeleteComponent],
  imports: [
    CommonModule,
    TranslateModule,
    RouterModule,
    OwlDateTimeModule,
    CompliancesRouting,
    OwlDateTimeModule,
    OwlMomentDateTimeModule,
    EformSharedModule,
    MtxGridModule,
    MatButtonModule,
    MatDialogModule,
  ],
  providers: [
    {
      provide: OWL_DATE_TIME_FORMATS,
      useValue: MY_MOMENT_FORMATS_FOR_BACKEND_CONFIGURATIONS_PLUGIN,
    },
  ],
})
export class CompliancesModule {
}
