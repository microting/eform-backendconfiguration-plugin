import {NgModule} from '@angular/core';
import {CommonModule} from '@angular/common';
import {FormsModule, ReactiveFormsModule} from '@angular/forms';
import {TranslateModule} from '@ngx-translate/core';
import {MatFormFieldModule} from '@angular/material/form-field';
import {MatInputModule} from '@angular/material/input';
import {MatButtonModule} from '@angular/material/button';
import {MatIconModule} from '@angular/material/icon';
import {MatCardModule} from '@angular/material/card';
import {MatDialogModule} from '@angular/material/dialog';
import {MatDatepickerModule} from '@angular/material/datepicker';
import {MatRadioModule} from '@angular/material/radio';
import {MtxGridModule} from '@ng-matero/extensions/grid';
import {MtxSelectModule} from '@ng-matero/extensions/select';
import {EformSharedModule} from 'src/app/common/modules/eform-shared/eform-shared.module';
// Declares the shared tag list/create/edit/delete/bulk-create dialogs opened by
// TaskListTagsComponent. Deliberately NOT PlanningsModule (which exports the
// equivalent PlanningTagsComponent) — that one ships a RouterModule.forChild
// with a '' route that would collide with this lazy module's own routing.
import {EformSharedTagsModule} from 'src/app/common/modules/eform-shared-tags/eform-shared-tags.module';
import {CalendarModule} from '../calendar/calendar.module';
import {TaskListRouting} from './task-list.routing';
import {
  TaskListPageComponent,
  TaskListFiltersComponent,
  TaskListTableComponent,
  TaskListTagsComponent,
  BatchWorkerModalComponent,
  BatchEformModalComponent,
  BatchTagsModalComponent,
  BatchComplianceModalComponent,
  BatchCopyModalComponent,
  BatchStartDateModalComponent,
  BatchStatusModalComponent,
  BatchDeleteModalComponent,
} from './components';

@NgModule({
  imports: [
    CommonModule,
    TaskListRouting,
    EformSharedModule,
    EformSharedTagsModule,
    TranslateModule,
    FormsModule,
    ReactiveFormsModule,
    MatFormFieldModule,
    MatInputModule,
    MatButtonModule,
    MatIconModule,
    MatCardModule,
    MatDialogModule,
    MatDatepickerModule,
    MatRadioModule,
    MtxGridModule,
    MtxSelectModule,
    CalendarModule,
  ],
  declarations: [
    TaskListPageComponent,
    TaskListFiltersComponent,
    TaskListTableComponent,
    TaskListTagsComponent,
    BatchWorkerModalComponent,
    BatchEformModalComponent,
    BatchTagsModalComponent,
    BatchComplianceModalComponent,
    BatchCopyModalComponent,
    BatchStartDateModalComponent,
    BatchStatusModalComponent,
    BatchDeleteModalComponent,
  ],
})
export class TaskListModule {}
