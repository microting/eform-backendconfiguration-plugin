import {CommonModule} from '@angular/common';
import {NgModule} from '@angular/core';
import {FormsModule} from '@angular/forms';
import {MatCardModule} from '@angular/material/card';
import {MatCheckboxModule} from '@angular/material/checkbox';
import {MatFormFieldModule} from '@angular/material/form-field';
import {MatIconModule} from '@angular/material/icon';
import {MatInputModule} from '@angular/material/input';
import {MatMenuModule} from '@angular/material/menu';
import {RouterModule} from '@angular/router';
import {TranslateModule} from '@ngx-translate/core';
import {MtxSelectModule} from '@ng-matero/extensions/select';
import {ChemicalsRouting} from './chemicals.routing';
import {ChemicalPermissionsPageComponent, ChemicalsContainerComponent} from './components';

/**
 * Kemi web screens (flutter-chemistry spec section 11, W1-W6). Every Kemi component is
 * declared here; each screen task adds the modules its page needs.
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
    FormsModule,
    MatCardModule,
    MatCheckboxModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatMenuModule,
    MtxSelectModule,
  ],
})
export class ChemicalsModule {
}
