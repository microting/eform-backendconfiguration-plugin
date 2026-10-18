import {NgModule} from '@angular/core';
import {CommonModule} from '@angular/common';
import {FormsModule} from '@angular/forms';
import {EformSharedModule} from 'src/app/common/modules/eform-shared/eform-shared.module';
import {
  TaskWorkerAssignmentsPageComponent,
} from './components';
import {TaskWorkerAssignmentsRouting} from './task-worker-assignments.routing';
import {TranslateModule} from '@ngx-translate/core';
import {AreaRulePlanModalModule} from '../../components/area-rule-plan-modal.module';
import {MtxGridModule} from '@ng-matero/extensions/grid';
import {MatDialogModule} from '@angular/material/dialog';

@NgModule({
  imports: [
    CommonModule,
    TaskWorkerAssignmentsRouting,
    EformSharedModule,
    MatDialogModule,
    TranslateModule,
    FormsModule,
    AreaRulePlanModalModule,
    MtxGridModule,
    TaskWorkerAssignmentsPageComponent,
  ],
})
export class TaskWorkerAssignmentsModule {
}
