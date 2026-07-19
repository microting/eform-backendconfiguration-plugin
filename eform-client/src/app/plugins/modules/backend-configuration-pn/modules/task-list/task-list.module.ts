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
import {MtxGridModule} from '@ng-matero/extensions/grid';
import {MtxSelectModule} from '@ng-matero/extensions/select';
import {EformSharedModule} from 'src/app/common/modules/eform-shared/eform-shared.module';
import {CalendarModule} from '../calendar/calendar.module';
import {TaskListRouting} from './task-list.routing';
import {
  TaskListPageComponent,
  TaskListFiltersComponent,
  TaskListTableComponent,
  BatchWorkerModalComponent,
  BatchEformModalComponent,
  BatchTagsModalComponent,
  BatchCopyModalComponent,
  BatchDeleteModalComponent,
} from './components';

@NgModule({
  imports: [
    CommonModule,
    TaskListRouting,
    EformSharedModule,
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
    MtxGridModule,
    MtxSelectModule,
    CalendarModule,
  ],
  declarations: [
    TaskListPageComponent,
    TaskListFiltersComponent,
    TaskListTableComponent,
    BatchWorkerModalComponent,
    BatchEformModalComponent,
    BatchTagsModalComponent,
    BatchCopyModalComponent,
    BatchDeleteModalComponent,
  ],
})
export class TaskListModule {}
