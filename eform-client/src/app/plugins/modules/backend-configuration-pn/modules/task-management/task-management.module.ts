import {CommonModule} from '@angular/common';
import {NgModule} from '@angular/core';
import {RouterModule} from '@angular/router';
import {TranslateModule} from '@ngx-translate/core';
import {
  TaskManagementContainerComponent,
  TaskManagementTableComponent,
  TaskManagementFiltersComponent,
  TaskManagementCreateShowModalComponent,
  TaskManagementDeleteModalComponent,
} from './components';
import {TaskManagementRouting} from './task-management.routing';
import {EformSharedModule} from 'src/app/common/modules/eform-shared/eform-shared.module';
import {FormsModule, ReactiveFormsModule} from '@angular/forms';
import {EformImportedModule} from 'src/app/common/modules/eform-imported/eform-imported.module';
import {MatButtonModule} from '@angular/material/button';
import {MatTooltipModule} from '@angular/material/tooltip';
import {MatIconModule} from '@angular/material/icon';
import {MatFormFieldModule} from '@angular/material/form-field';
import {MtxSelectModule} from '@ng-matero/extensions/select';
import {MatInputModule} from '@angular/material/input';
import {MtxGridModule} from '@ng-matero/extensions/grid';
import {MatDialogModule} from '@angular/material/dialog';
import {MatCardModule} from '@angular/material/card';
import {MatDatepickerModule} from '@angular/material/datepicker';
import {StatisticsModule} from '../statistics/statistics.module';
import {MatChip} from "@angular/material/chips";

@NgModule({
  declarations: [
    TaskManagementContainerComponent,
    TaskManagementTableComponent,
    TaskManagementFiltersComponent,
    TaskManagementCreateShowModalComponent,
    TaskManagementDeleteModalComponent
  ],
    imports: [
        CommonModule,
        TranslateModule,
        RouterModule,
        TaskManagementRouting,
        EformSharedModule,
        ReactiveFormsModule,
        EformImportedModule,
        FormsModule,
        MatButtonModule,
        MatTooltipModule,
        MatIconModule,
        MatFormFieldModule,
        MtxSelectModule,
        MatInputModule,
        MtxGridModule,
        MatDialogModule,
        MatCardModule,
        MatDatepickerModule,
        StatisticsModule,
        MatChip,
    ],
  providers: [],
})
export class TaskManagementModule {
}
