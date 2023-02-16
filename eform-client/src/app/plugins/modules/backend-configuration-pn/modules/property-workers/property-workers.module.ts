import {NgModule} from '@angular/core';
import {CommonModule} from '@angular/common';
import {FormsModule} from '@angular/forms';
import {EformSharedModule} from 'src/app/common/modules/eform-shared/eform-shared.module';
import {
  PropertyWorkerCreateEditModalComponent,
  PropertyWorkerDeleteModalComponent,
  PropertyWorkersPageComponent,
  PropertyWorkerOtpModalComponent,
} from './components';
import {PropertyWorkersRouting} from './property-workers.routing';
import {TranslateModule} from '@ngx-translate/core';
import {DeviceUsersPersistProvider} from 'src/app/modules/device-users/components/store';
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
    ],
  declarations: [
    PropertyWorkersPageComponent,
    PropertyWorkerOtpModalComponent,
    PropertyWorkerDeleteModalComponent,
    PropertyWorkerCreateEditModalComponent,
  ],
  providers: [DeviceUsersPersistProvider],
})
export class PropertyWorkersModule {
}
