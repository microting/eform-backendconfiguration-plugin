import {NgModule} from '@angular/core';
import {CommonModule} from '@angular/common';
import {FormsModule, ReactiveFormsModule} from '@angular/forms';
import {EformSharedModule} from 'src/app/common/modules/eform-shared/eform-shared.module';
import {
  TaskWizardCreateModalComponent,
  TaskWizardFiltersComponent,
  TaskWizardFoldersModalComponent, TaskWizardMultipleDeactivateComponent,
  TaskWizardPageComponent,
  TaskWizardTableComponent,
  TaskWizardUpdateModalComponent,
} from './components';
import {TaskWizardRouting} from './task-wizard.routing';
import {TranslateModule} from '@ngx-translate/core';
import {MatFormFieldModule} from '@angular/material/form-field';
import {MtxSelectModule} from '@ng-matero/extensions/select';
import {MatButtonModule} from '@angular/material/button';
import {MatIconModule} from '@angular/material/icon';
import {MatTooltipModule} from '@angular/material/tooltip';
import {MtxGridModule} from '@ng-matero/extensions/grid';
import {MatChipsModule} from '@angular/material/chips';
import {MatDialogModule} from '@angular/material/dialog';
import {MatDatepickerModule} from '@angular/material/datepicker';
import {MatInputModule} from '@angular/material/input';
import {MatSlideToggleModule} from '@angular/material/slide-toggle';
import {MatCheckboxModule} from '@angular/material/checkbox';
import {FormattingTextEditorModule} from 'src/app/common/modules/eform-imported/formatting-text-editor/formatting-text-editor.module';
import {MatCardModule} from '@angular/material/card';
import {PlanningsModule} from '../../../items-planning-pn/modules/plannings/plannings.module';
import {StatisticsModule} from '../statistics/statistics.module';

@NgModule({
  imports: [
    CommonModule,
    TaskWizardRouting,
    EformSharedModule,
    TranslateModule,
    FormsModule,
    MatFormFieldModule,
    MtxSelectModule,
    MatButtonModule,
    MatIconModule,
    MatTooltipModule,
    ReactiveFormsModule,
    MtxGridModule,
    MatChipsModule,
    MatDialogModule,
    MatDatepickerModule,
    MatInputModule,
    MatSlideToggleModule,
    MatCheckboxModule,
    FormattingTextEditorModule,
    MatCardModule,
    PlanningsModule,
    StatisticsModule,
  ],
  declarations: [
    TaskWizardFiltersComponent,
    TaskWizardPageComponent,
    TaskWizardTableComponent,
    TaskWizardCreateModalComponent,
    TaskWizardUpdateModalComponent,
    TaskWizardFoldersModalComponent,
    TaskWizardMultipleDeactivateComponent,
  ],
})
export class TaskWizardModule {
}
