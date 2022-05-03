import {CommonModule} from '@angular/common';
import {NgModule} from '@angular/core';
import {RouterModule} from '@angular/router';
import {
  OwlDateTimeModule,
  OwlMomentDateTimeModule,
  OWL_DATE_TIME_FORMATS,
} from '@danielmoncada/angular-datetime-picker';
import {TranslateModule} from '@ngx-translate/core';
import {MDBBootstrapModule} from 'angular-bootstrap-md';
import {
  TaskManagementContainerComponent,
  TaskManagementTableComponent,
  TaskManagementFiltersComponent,
  TaskManagementCreateShowModalComponent,
  TaskManagementDeleteModalComponent,
} from './components';
import {TaskManagementRouting} from './task-management.routing';
import {MY_MOMENT_FORMATS_FOR_BACKEND_CONFIGURATIONS_PLUGIN} from '../../consts/custom-date-time-adapter';
import {EformSharedModule} from 'src/app/common/modules/eform-shared/eform-shared.module';
import {FontAwesomeModule} from '@fortawesome/angular-fontawesome';
import {FormsModule, ReactiveFormsModule} from '@angular/forms';
import {NgSelectModule} from '@ng-select/ng-select';
import {EformImportedModule} from 'src/app/common/modules/eform-imported/eform-imported.module';

@NgModule({
  declarations: [
    TaskManagementContainerComponent,
    TaskManagementTableComponent,
    TaskManagementFiltersComponent,
    TaskManagementCreateShowModalComponent,
    TaskManagementDeleteModalComponent
  ],
  imports: [
    CommonModule,
    MDBBootstrapModule,
    TranslateModule,
    RouterModule,
    OwlDateTimeModule,
    TaskManagementRouting,
    OwlDateTimeModule,
    OwlMomentDateTimeModule,
    EformSharedModule,
    FontAwesomeModule,
    ReactiveFormsModule,
    NgSelectModule,
    EformImportedModule,
    FormsModule,
  ],
  providers: [
    {
      provide: OWL_DATE_TIME_FORMATS,
      useValue: MY_MOMENT_FORMATS_FOR_BACKEND_CONFIGURATIONS_PLUGIN,
    },
  ],
})
export class TaskManagementModule {}
