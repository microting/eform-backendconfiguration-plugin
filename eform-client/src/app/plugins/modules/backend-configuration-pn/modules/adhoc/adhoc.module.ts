import {CommonModule} from '@angular/common';
import {NgModule} from '@angular/core';
import {RouterModule} from '@angular/router';
import {TranslateModule} from '@ngx-translate/core';
import {FormsModule, ReactiveFormsModule} from '@angular/forms';
import {EformSharedModule} from 'src/app/common/modules/eform-shared/eform-shared.module';
import {MatButtonModule} from '@angular/material/button';
import {MatTooltipModule} from '@angular/material/tooltip';
import {MatIconModule} from '@angular/material/icon';
import {MatCardModule} from '@angular/material/card';
import {MatFormFieldModule} from '@angular/material/form-field';
import {MatInputModule} from '@angular/material/input';
import {MtxSelectModule} from '@ng-matero/extensions/select';
import {MtxGridModule} from '@ng-matero/extensions/grid';
import {MatDialogModule} from '@angular/material/dialog';
import {MatMenu, MatMenuItem, MatMenuTrigger} from '@angular/material/menu';
import {MatDatepickerModule} from '@angular/material/datepicker';
import {MatChip} from '@angular/material/chips';
import {MatExpansionModule} from '@angular/material/expansion';
import {MatCheckboxModule} from '@angular/material/checkbox';
import {AdhocRouting} from './adhoc.routing';
import {AdhocContainerComponent} from './components';

/**
 * Adhoc dashboard module (M5/F4) - import set copied from
 * task-management.module.ts, since the follow-up F5-F8 tasks (table,
 * filters, drawer, modals/history) will need most of it. Only
 * AdhocContainerComponent is declared/built so far; F5-F8 add their
 * components' declarations here as they land.
 */
@NgModule({
  declarations: [
    AdhocContainerComponent,
  ],
  imports: [
    CommonModule,
    TranslateModule,
    RouterModule,
    AdhocRouting,
    EformSharedModule,
    FormsModule,
    ReactiveFormsModule,
    MatButtonModule,
    MatTooltipModule,
    MatIconModule,
    MatCardModule,
    MatFormFieldModule,
    MatInputModule,
    MtxSelectModule,
    MtxGridModule,
    MatDialogModule,
    MatMenu,
    MatMenuItem,
    MatMenuTrigger,
    MatDatepickerModule,
    MatChip,
    MatExpansionModule,
    MatCheckboxModule,
  ],
  providers: [],
})
export class AdhocModule {
}
