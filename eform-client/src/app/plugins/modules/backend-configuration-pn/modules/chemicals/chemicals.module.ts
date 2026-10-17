import {DragDropModule} from '@angular/cdk/drag-drop';
import {CommonModule} from '@angular/common';
import {NgModule} from '@angular/core';
import {FormsModule} from '@angular/forms';
import {MatCardModule} from '@angular/material/card';
import {MatCheckboxModule} from '@angular/material/checkbox';
import {MatChipsModule} from '@angular/material/chips';
import {MatDialogModule} from '@angular/material/dialog';
import {MatFormFieldModule} from '@angular/material/form-field';
import {MatIconModule} from '@angular/material/icon';
import {MatInputModule} from '@angular/material/input';
import {MatMenuModule} from '@angular/material/menu';
import {MatPaginatorModule} from '@angular/material/paginator';
import {MatRadioModule} from '@angular/material/radio';
import {MatSlideToggleModule} from '@angular/material/slide-toggle';
import {MatTooltipModule} from '@angular/material/tooltip';
import {RouterModule} from '@angular/router';
import {TranslateModule} from '@ngx-translate/core';
import {MtxGridModule} from '@ng-matero/extensions/grid';
import {MtxSelectModule} from '@ng-matero/extensions/select';
import {EformSharedModule} from 'src/app/common/modules/eform-shared/eform-shared.module';
import {ChemicalsRouting} from './chemicals.routing';
import {
  ChemicalHistoryPageComponent,
  ChemicalInventoryPageComponent,
  ChemicalLocationDialogComponent,
  ChemicalLocationsPageComponent,
  ChemicalMoveDialogComponent,
  ChemicalNoteDialogComponent,
  ChemicalPermissionsPageComponent,
  ChemicalProductDrawerComponent,
  ChemicalRegisterPlacementDialogComponent,
  ChemicalRemoveDialogComponent,
  ChemicalSettingsPageComponent,
  ChemicalStockDialogComponent,
  ChemicalsContainerComponent,
} from './components';

/**
 * Kemi web screens (flutter-chemistry spec section 11, W1-W6). Every Kemi component is
 * declared here; each screen task adds the modules its page needs.
 */
@NgModule({
  declarations: [
    ChemicalsContainerComponent,
    ChemicalInventoryPageComponent,
    ChemicalRegisterPlacementDialogComponent,
    ChemicalProductDrawerComponent,
    ChemicalMoveDialogComponent,
    ChemicalRemoveDialogComponent,
    ChemicalNoteDialogComponent,
    ChemicalStockDialogComponent,
    ChemicalLocationsPageComponent,
    ChemicalLocationDialogComponent,
    ChemicalPermissionsPageComponent,
    ChemicalSettingsPageComponent,
    ChemicalHistoryPageComponent,
  ],
  imports: [
    CommonModule,
    TranslateModule,
    RouterModule,
    ChemicalsRouting,
    FormsModule,
    DragDropModule,
    EformSharedModule,
    MatCardModule,
    MatCheckboxModule,
    MatChipsModule,
    MatDialogModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatMenuModule,
    MatPaginatorModule,
    MatRadioModule,
    MatSlideToggleModule,
    MatTooltipModule,
    MtxGridModule,
    MtxSelectModule,
  ],
})
export class ChemicalsModule {
}
