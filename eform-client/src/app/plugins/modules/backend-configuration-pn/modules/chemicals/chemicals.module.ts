import {CommonModule} from '@angular/common';
import {NgModule} from '@angular/core';
import {FormsModule, ReactiveFormsModule} from '@angular/forms';
import {RouterModule} from '@angular/router';
import {TranslateModule} from '@ngx-translate/core';
import {DragDropModule} from '@angular/cdk/drag-drop';
import {MatButtonModule} from '@angular/material/button';
import {MatCardModule} from '@angular/material/card';
import {MatCheckboxModule} from '@angular/material/checkbox';
import {MatChipsModule} from '@angular/material/chips';
import {MatDatepickerModule} from '@angular/material/datepicker';
import {MatDialogModule} from '@angular/material/dialog';
import {MatFormFieldModule} from '@angular/material/form-field';
import {MatIconModule} from '@angular/material/icon';
import {MatInputModule} from '@angular/material/input';
import {MatMenuModule} from '@angular/material/menu';
import {MatRadioModule} from '@angular/material/radio';
import {MatSlideToggleModule} from '@angular/material/slide-toggle';
import {MatTooltipModule} from '@angular/material/tooltip';
import {MtxGridModule} from '@ng-matero/extensions/grid';
import {MtxSelectModule} from '@ng-matero/extensions/select';
import {EformSharedModule} from 'src/app/common/modules/eform-shared/eform-shared.module';
import {ChemicalsRouting} from './chemicals.routing';
import {ChemicalPermissionsPageComponent, ChemicalsContainerComponent} from './components';

/**
 * Kemi web screens (flutter-chemistry spec section 11, W1-W6). Every Kemi component is
 * declared here; the import set covers all of them (grid, dialogs, chips, drag-drop)
 * so later screen tasks only add declarations.
 */
@NgModule({
  declarations: [
    ChemicalsContainerComponent,
    ChemicalPermissionsPageComponent,
  ],
  imports: [
    CommonModule,
    TranslateModule,
    RouterModule,
    ChemicalsRouting,
    EformSharedModule,
    FormsModule,
    ReactiveFormsModule,
    DragDropModule,
    MatButtonModule,
    MatCardModule,
    MatCheckboxModule,
    MatChipsModule,
    MatDatepickerModule,
    MatDialogModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatMenuModule,
    MatRadioModule,
    MatSlideToggleModule,
    MatTooltipModule,
    MtxGridModule,
    MtxSelectModule,
  ],
})
export class ChemicalsModule {
}
