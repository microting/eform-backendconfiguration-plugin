import { NgModule } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MDBBootstrapModule } from 'angular-bootstrap-md';
import { EformSharedModule } from 'src/app/common/modules/eform-shared/eform-shared.module';
import {
  TaskWorkerAssignmentsPageComponent,
} from './components';
import { TaskWorkerAssignmentsRouting} from './task-worker-assignments.routing';
import { TranslateModule } from '@ngx-translate/core';
import { FontAwesomeModule } from '@fortawesome/angular-fontawesome';

@NgModule({
  imports: [
    CommonModule,
    TaskWorkerAssignmentsRouting,
    MDBBootstrapModule,
    EformSharedModule,
    TranslateModule,
    FormsModule,
    FontAwesomeModule,
  ],
  declarations: [
    TaskWorkerAssignmentsPageComponent
  ],
})
export class TaskWorkerAssignmentsModule {}
