import {NgModule} from '@angular/core';
import {CommonModule} from '@angular/common';
import {TranslateModule} from '@ngx-translate/core';
import {RouterModule} from '@angular/router';
import {EformSharedModule} from 'src/app/common/modules/eform-shared/eform-shared.module';
import {FormsModule} from '@angular/forms';
import {
  AdHocTaskPrioritiesComponent,
  DocumentUpdatedDaysComponent,
  PlannedTaskDaysComponent,
  StatisticsContainerComponent,
  PlannedTaskWorkersComponent,
  AdHocTaskWorkersComponent,
} from './components';
import {StatisticsRouting} from './statistics.routing';
import {statisticsPersistProvider} from './store';
import {NgxChartsModule} from '@swimlane/ngx-charts';
import {MatCardModule} from '@angular/material/card';
import {MatFormFieldModule} from '@angular/material/form-field';
import {MtxSelectModule} from '@ng-matero/extensions/select';

@NgModule({
  declarations: [
    StatisticsContainerComponent,
    PlannedTaskDaysComponent,
    AdHocTaskPrioritiesComponent,
    DocumentUpdatedDaysComponent,
    PlannedTaskWorkersComponent,
    AdHocTaskWorkersComponent,
  ],
  imports: [
    CommonModule,
    TranslateModule,
    RouterModule,
    StatisticsRouting,
    EformSharedModule,
    FormsModule,
    NgxChartsModule,
    MatCardModule,
    MatFormFieldModule,
    MtxSelectModule,
  ],
  providers: [
    statisticsPersistProvider,
  ],
  exports: [
    PlannedTaskDaysComponent,
    AdHocTaskPrioritiesComponent,
    AdHocTaskWorkersComponent,
    PlannedTaskWorkersComponent,
    DocumentUpdatedDaysComponent
  ]
})

export class StatisticsModule {
}
