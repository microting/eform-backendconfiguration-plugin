import {NgModule} from '@angular/core';
import {CommonModule} from '@angular/common';
import {FormsModule} from '@angular/forms';
import {NgSelectModule} from '@ng-select/ng-select';
import {TranslateModule} from '@ngx-translate/core';
import { MDBBootstrapModule } from 'angular-bootstrap-md';
import {
  AreaRulePlanModalComponent
} from './';
import {FontAwesomeModule} from '@fortawesome/angular-fontawesome';
import {OWL_DATE_TIME_FORMATS, OwlDateTimeModule} from '@danielmoncada/angular-datetime-picker';
import { MY_MOMENT_FORMATS_FOR_BACKEND_CONFIGURATIONS_PLUGIN } from '../consts/custom-date-time-adapter';

@NgModule({
  imports: [
    OwlDateTimeModule,
    CommonModule,
    FormsModule,
    TranslateModule,
    NgSelectModule,
    MDBBootstrapModule,
    FontAwesomeModule,
  ],
  declarations: [AreaRulePlanModalComponent],
  exports: [AreaRulePlanModalComponent],
  providers: [
    {
      provide: OWL_DATE_TIME_FORMATS,
      useValue: MY_MOMENT_FORMATS_FOR_BACKEND_CONFIGURATIONS_PLUGIN,
    },
  ],
})
export class AreaRulePlanModalModule {}
