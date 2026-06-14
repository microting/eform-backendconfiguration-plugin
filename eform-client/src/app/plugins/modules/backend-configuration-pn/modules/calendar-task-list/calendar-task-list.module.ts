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
import {MtxGridModule} from '@ng-matero/extensions/grid';
import {MtxSelectModule} from '@ng-matero/extensions/select';
import {EformSharedModule} from 'src/app/common/modules/eform-shared/eform-shared.module';
import {CalendarModule} from '../calendar/calendar.module';
import {CalendarTaskListRouting} from './calendar-task-list.routing';
import {
  CalendarTaskListPageComponent,
  CalendarTaskListFiltersComponent,
  CalendarTaskListTableComponent,
} from './components';

@NgModule({
  imports: [
    CommonModule,
    CalendarTaskListRouting,
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
    MtxGridModule,
    MtxSelectModule,
    CalendarModule,
  ],
  declarations: [
    CalendarTaskListPageComponent,
    CalendarTaskListFiltersComponent,
    CalendarTaskListTableComponent,
  ],
})
export class CalendarTaskListModule {}
