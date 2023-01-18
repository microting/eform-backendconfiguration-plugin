import {NgModule} from '@angular/core';
import {CommonModule} from '@angular/common';
import {FormsModule} from '@angular/forms';
import {EformSharedModule} from 'src/app/common/modules/eform-shared/eform-shared.module';
import {
  TaskWorkerAssignmentsPageComponent,
} from './components';
import {TaskWorkerAssignmentsRouting} from './task-worker-assignments.routing';
import {TranslateModule} from '@ngx-translate/core';
import {FontAwesomeModule} from '@fortawesome/angular-fontawesome';
import {AreaRulePlanModalModule} from '../../components/area-rule-plan-modal.module';
import {MtxGridModule} from '@ng-matero/extensions/grid';

@NgModule({
  imports: [
    CommonModule,
    TaskWorkerAssignmentsRouting,
    EformSharedModule,
    TranslateModule,
    FormsModule,
    FontAwesomeModule,
    AreaRulePlanModalModule,
    MtxGridModule,
  ],
  declarations: [
    TaskWorkerAssignmentsPageComponent,
  ],
})
export class TaskWorkerAssignmentsModule {
}
