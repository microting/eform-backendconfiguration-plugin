import {NgModule} from '@angular/core';
import {CommonModule} from '@angular/common';
import {FormsModule, ReactiveFormsModule} from '@angular/forms';
import {EformSharedModule} from 'src/app/common/modules/eform-shared/eform-shared.module';
import {
  PropertyWorkerCreateEditModalComponent,
  PropertyWorkerDeleteModalComponent,
  PropertyWorkersPageComponent,
  PropertyWorkerOtpModalComponent, PropertyWorkerTableComponent,
} from './components';
import {PropertyWorkersRouting} from './property-workers.routing';
import {TranslateModule} from '@ngx-translate/core';
import {MatIconModule} from '@angular/material/icon';
import {MatButtonModule} from '@angular/material/button';
import {MatTooltipModule} from '@angular/material/tooltip';
import {MtxGridModule} from '@ng-matero/extensions/grid';
import {MatInputModule} from '@angular/material/input';
import {MatDialogModule} from '@angular/material/dialog';
import {MtxSelectModule} from '@ng-matero/extensions/select';
import {MatSlideToggleModule} from '@angular/material/slide-toggle';
import {MatCheckboxModule} from '@angular/material/checkbox';
import {MatMenuModule} from '@angular/material/menu';
import {
  PropertyWorkerFiltersComponent
} from 'src/app/plugins/modules/backend-configuration-pn/modules/property-workers/components/property-worker-filters/property-worker-filters.component';
import {MatChip} from "@angular/material/chips";
import {MatDatepicker, MatDatepickerInput, MatDatepickerToggle} from "@angular/material/datepicker";
import {MatTab, MatTabGroup} from "@angular/material/tabs";
import {EformSharedTagsModule} from "src/app/common/modules/eform-shared-tags/eform-shared-tags.module";
import {NgxMaterialTimepickerModule} from 'ngx-material-timepicker';

@NgModule({
  imports: [
    CommonModule,
    PropertyWorkersRouting,
    EformSharedModule,
    TranslateModule,
    FormsModule,
    MatIconModule,
    MatButtonModule,
    MatTooltipModule,
    MtxGridModule,
    MatInputModule,
    MatDialogModule,
    MtxSelectModule,
    MatSlideToggleModule,
    MatCheckboxModule,
    MatMenuModule,
    ReactiveFormsModule,
    MatChip,
    MatDatepicker,
    MatDatepickerInput,
    MatDatepickerToggle,
    MatTab,
    MatTabGroup,
    EformSharedTagsModule,
    NgxMaterialTimepickerModule,
  ],
  declarations: [
    PropertyWorkersPageComponent,
    PropertyWorkerOtpModalComponent,
    PropertyWorkerDeleteModalComponent,
    PropertyWorkerCreateEditModalComponent,
    PropertyWorkerTableComponent,
    PropertyWorkerFiltersComponent
  ],
})
export class PropertyWorkersModule {
}
