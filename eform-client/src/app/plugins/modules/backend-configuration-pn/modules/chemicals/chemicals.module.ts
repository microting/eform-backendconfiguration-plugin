import {CommonModule} from '@angular/common';
import {NgModule} from '@angular/core';
import {FormsModule} from '@angular/forms';
import {MatCardModule} from '@angular/material/card';
import {MatCheckboxModule} from '@angular/material/checkbox';
import {MatChipsModule} from '@angular/material/chips';
import {MatFormFieldModule} from '@angular/material/form-field';
import {MatIconModule} from '@angular/material/icon';
import {MatInputModule} from '@angular/material/input';
import {MatMenuModule} from '@angular/material/menu';
import {MatSlideToggleModule} from '@angular/material/slide-toggle';
import {RouterModule} from '@angular/router';
import {TranslateModule} from '@ngx-translate/core';
import {MtxSelectModule} from '@ng-matero/extensions/select';
import {ChemicalsRouting} from './chemicals.routing';
import {ChemicalPermissionsPageComponent, ChemicalSettingsPageComponent, ChemicalsContainerComponent} from './components';

/**
 * Kemi web screens (flutter-chemistry spec section 11, W1-W6). Every Kemi component is
 * declared here; each screen task adds the modules its page needs.
 */
@NgModule({
  declarations: [
    ChemicalsContainerComponent,
    ChemicalPermissionsPageComponent,
    ChemicalSettingsPageComponent,
  ],
  imports: [
    CommonModule,
    TranslateModule,
    RouterModule,
    ChemicalsRouting,
    FormsModule,
    MatCardModule,
    MatCheckboxModule,
    MatChipsModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatMenuModule,
    MatSlideToggleModule,
    MtxSelectModule,
  ],
})
export class ChemicalsModule {
}
