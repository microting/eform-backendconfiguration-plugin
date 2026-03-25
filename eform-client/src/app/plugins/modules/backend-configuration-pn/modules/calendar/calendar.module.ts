import {CommonModule} from '@angular/common';
import {NgModule} from '@angular/core';
import {FormsModule, ReactiveFormsModule} from '@angular/forms';
import {RouterModule} from '@angular/router';
import {TranslateModule} from '@ngx-translate/core';
import {DragDropModule} from '@angular/cdk/drag-drop';
import {OverlayModule} from '@angular/cdk/overlay';
import {PortalModule} from '@angular/cdk/portal';
import {MatButtonModule} from '@angular/material/button';
import {MatButtonToggleModule} from '@angular/material/button-toggle';
import {MatCardModule} from '@angular/material/card';
import {MatCheckboxModule} from '@angular/material/checkbox';
import {MatChipsModule} from '@angular/material/chips';
import {MatDatepickerModule} from '@angular/material/datepicker';
import {MatNativeDateModule} from '@angular/material/core';
import {MatDialogModule} from '@angular/material/dialog';
import {MatExpansionModule} from '@angular/material/expansion';
import {MatFormFieldModule} from '@angular/material/form-field';
import {MatIconModule} from '@angular/material/icon';
import {MatInputModule} from '@angular/material/input';
import {MatListModule} from '@angular/material/list';
import {MatMenuModule} from '@angular/material/menu';
import {MatRadioModule} from '@angular/material/radio';
import {MatSelectModule} from '@angular/material/select';
import {MatTooltipModule} from '@angular/material/tooltip';
import {MtxSelectModule} from '@ng-matero/extensions/select';
import {MtxGridModule} from '@ng-matero/extensions/grid';
import {EformSharedModule} from 'src/app/common/modules/eform-shared/eform-shared.module';
import {EformImportedModule} from 'src/app/common/modules/eform-imported/eform-imported.module';
import {TeamCreateDialogComponent} from './components/calendar-sidebar/team-create-dialog.component';
import {TeamDeleteDialogComponent} from './components/calendar-sidebar/team-delete-dialog.component';

import {CalendarRouting} from './calendar.routing';
import {
  CalendarContainerComponent,
  CalendarDayColumnComponent,
  CalendarHeaderComponent,
  CalendarMiniCalendarComponent,
  CalendarScheduleViewComponent,
  CalendarSidebarComponent,
  CalendarTaskBlockComponent,
  CalendarWeekGridComponent,
} from './components';
import {
  TaskCreateEditModalComponent,
  TaskPreviewModalComponent,
  TaskDeleteModalComponent,
  RepeatScopeModalComponent,
  CustomRepeatModalComponent,
  BoardCreateModalComponent,
  BoardDeleteModalComponent,
} from './modals';

// Re-export modal components for barrel
export {
  TaskCreateEditModalComponent,
  TaskPreviewModalComponent,
  TaskDeleteModalComponent,
  RepeatScopeModalComponent,
  CustomRepeatModalComponent,
  BoardCreateModalComponent,
  BoardDeleteModalComponent,
};

@NgModule({
  declarations: [
    // Components
    CalendarContainerComponent,
    CalendarDayColumnComponent,
    CalendarHeaderComponent,
    CalendarSidebarComponent,
    CalendarWeekGridComponent,
    CalendarTaskBlockComponent,
    CalendarScheduleViewComponent,
    CalendarMiniCalendarComponent,
    // Modals
    TaskCreateEditModalComponent,
    TaskPreviewModalComponent,
    TaskDeleteModalComponent,
    RepeatScopeModalComponent,
    CustomRepeatModalComponent,
    BoardCreateModalComponent,
    BoardDeleteModalComponent,
    TeamCreateDialogComponent,
    TeamDeleteDialogComponent,
  ],
  imports: [
    CommonModule,
    FormsModule,
    ReactiveFormsModule,
    RouterModule,
    TranslateModule,
    CalendarRouting,
    DragDropModule,
    OverlayModule,
    PortalModule,
    EformSharedModule,
    EformImportedModule,
    MatButtonModule,
    MatButtonToggleModule,
    MatCardModule,
    MatCheckboxModule,
    MatChipsModule,
    MatDatepickerModule,
    MatNativeDateModule,
    MatDialogModule,
    MatExpansionModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatListModule,
    MatMenuModule,
    MatRadioModule,
    MatSelectModule,
    MatTooltipModule,
    MtxSelectModule,
    MtxGridModule,
  ],
  providers: [],
})
export class CalendarModule {}
