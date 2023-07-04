import {NgModule} from '@angular/core';
import {CommonModule} from '@angular/common';
import {FormsModule, ReactiveFormsModule} from '@angular/forms';
import {EformSharedModule} from 'src/app/common/modules/eform-shared/eform-shared.module';
import {
  TaskWizardFiltersComponent, TaskWizardPageComponent,
} from './components';
import {TaskWizardRouting} from './task-wizard.routing';
import {TranslateModule} from '@ngx-translate/core';
import {MatFormFieldModule} from '@angular/material/form-field';
import {MtxSelectModule} from '@ng-matero/extensions/select';
import {MatButtonModule} from '@angular/material/button';
import {MatIconModule} from '@angular/material/icon';
import {MatTooltipModule} from '@angular/material/tooltip';

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
  ],
  declarations: [
    TaskWizardFiltersComponent,
    TaskWizardPageComponent,
  ],
})
export class TaskWizardModule {
}
