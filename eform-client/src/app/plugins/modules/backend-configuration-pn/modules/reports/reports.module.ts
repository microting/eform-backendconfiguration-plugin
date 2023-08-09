import {NgModule} from '@angular/core';
import {CommonModule} from '@angular/common';
import {ReportsRouting} from './reports.routing';
import {EformSharedModule} from 'src/app/common/modules/eform-shared/eform-shared.module';
import {TranslateModule} from '@ngx-translate/core';
import {FormsModule, ReactiveFormsModule} from '@angular/forms';
import {MatIconModule} from '@angular/material/icon';
import {MatButtonModule} from '@angular/material/button';
import {MatTooltipModule} from '@angular/material/tooltip';
import {MtxGridModule} from '@ng-matero/extensions/grid';
import {MatInputModule} from '@angular/material/input';
import {MatDialogModule} from '@angular/material/dialog';
import {MtxSelectModule} from '@ng-matero/extensions/select';
import {MatSlideToggleModule} from '@angular/material/slide-toggle';
import {MatCheckboxModule} from '@angular/material/checkbox';
import {MatMenuModule} from '@angular/material/menu';
import {
  CaseDeleteComponent,
  ReportContainerComponent,
  ReportHeaderComponent,
  ReportTableComponent
} from './components';
import {planningsReportPersistProvider} from './components/store';
import {MatDatepickerModule} from '@angular/material/datepicker';


@NgModule({
  imports: [
    CommonModule,
    ReportsRouting,
    EformSharedModule,
    TranslateModule,
    FormsModule,
    MatIconModule,
    MatButtonModule,
    MatTooltipModule,
    MtxGridModule,
    MatInputModule,
    MatDialogModule,
    MtxSelectModule,
    MatSlideToggleModule,
    MatCheckboxModule,
    MatMenuModule,
    ReactiveFormsModule,
    MatDatepickerModule,
  ],
  declarations: [
    CaseDeleteComponent,
    ReportContainerComponent,
    ReportHeaderComponent,
    ReportTableComponent
  ],
  providers: [planningsReportPersistProvider],
})
export class ReportsModule {
}
