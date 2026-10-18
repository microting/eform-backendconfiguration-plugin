import {CommonModule} from '@angular/common';
import {NgModule} from '@angular/core';
import {RouterModule} from '@angular/router';
import {TranslateModule} from '@ngx-translate/core';
import {
  TaskTrackerContainerComponent,
  TaskTrackerTableComponent,
  TaskTrackerFiltersComponent,
  TaskTrackerCreateShowModalComponent,
  TaskTrackerShownColumnsComponent, TaskTrackerSelectWorkerModalComponent
} from './components';
import {TaskTrackerRouting} from './task-tracker.routing';
import {EformSharedModule} from 'src/app/common/modules/eform-shared/eform-shared.module';
import {FormsModule, ReactiveFormsModule} from '@angular/forms';
import {MatButtonModule} from '@angular/material/button';
import {MatTooltipModule} from '@angular/material/tooltip';
import {MatIconModule} from '@angular/material/icon';
import {MatFormFieldModule} from '@angular/material/form-field';
import {MtxSelectModule} from '@ng-matero/extensions/select';
import {MatInputModule} from '@angular/material/input';
import {MtxGridModule} from '@ng-matero/extensions/grid';
import {MatDialogModule} from '@angular/material/dialog';
import {MatCardModule} from '@angular/material/card';
import {MatTableModule} from '@angular/material/table';
import {MatCheckboxModule} from '@angular/material/checkbox';
import {PlanningsModule} from '../../../items-planning-pn/modules/plannings/plannings.module';
import {StatisticsModule} from '../statistics/statistics.module';
import {MatChipsModule} from '@angular/material/chips';
import {NgSelectModule} from "@ng-select/ng-select";
import {MatMenu, MatMenuItem, MatMenuTrigger} from "@angular/material/menu";
import {AppIconComponent} from "src/app/components/icons/app-icon/app-icon.component";

@NgModule({
    imports: [
        CommonModule,
        TranslateModule,
        RouterModule,
        TaskTrackerRouting,
        EformSharedModule,
        ReactiveFormsModule,
        FormsModule,
        MatButtonModule,
        MatCheckboxModule,
        MatTooltipModule,
        MatIconModule,
        MatFormFieldModule,
        MtxSelectModule,
        MatInputModule,
        MtxGridModule,
        MatDialogModule,
        MatCardModule,
        MatTableModule,
        PlanningsModule,
        StatisticsModule,
        MatChipsModule,
        NgSelectModule,
        MatMenuTrigger,
        MatMenu,
        MatMenuItem,
        AppIconComponent,
        TaskTrackerContainerComponent,
        TaskTrackerTableComponent,
        TaskTrackerFiltersComponent,
        TaskTrackerCreateShowModalComponent,
        TaskTrackerShownColumnsComponent,
        TaskTrackerSelectWorkerModalComponent,
    ],
  providers: [],
})
export class TaskTrackerModule {
}
