import { NgModule } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { NgSelectModule } from '@ng-select/ng-select';
import { MDBBootstrapModule } from 'angular-bootstrap-md';
import { EformSharedModule } from 'src/app/common/modules/eform-shared/eform-shared.module';
import {
  PropertyWorkerCreateModalComponent,
  PropertyWorkerDeleteModalComponent,
  PropertyWorkersPageComponent,
  PropertyWorkerEditModalComponent,
  PropertyWorkerOtpModalComponent,
} from './components';
import { PropertyWorkersRouting } from './property-workers.routing';
import { TranslateModule } from '@ngx-translate/core';
import { FontAwesomeModule } from '@fortawesome/angular-fontawesome';

@NgModule({
  imports: [
    CommonModule,
    PropertyWorkersRouting,
    NgSelectModule,
    MDBBootstrapModule,
    EformSharedModule,
    TranslateModule,
    FormsModule,
    FontAwesomeModule,
  ],
  declarations: [
    PropertyWorkersPageComponent,
    PropertyWorkerOtpModalComponent,
    PropertyWorkerDeleteModalComponent,
    PropertyWorkerCreateModalComponent,
    PropertyWorkerEditModalComponent,
  ],
})
export class PropertyWorkersModule {}
