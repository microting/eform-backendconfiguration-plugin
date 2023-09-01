import {NgModule} from '@angular/core';
import {CommonModule} from '@angular/common';
import {TranslateModule} from '@ngx-translate/core';
import {RouterModule} from '@angular/router';
import {EformSharedModule} from 'src/app/common/modules/eform-shared/eform-shared.module';
import {FormsModule, ReactiveFormsModule} from '@angular/forms';
import {EformImportedModule} from 'src/app/common/modules/eform-imported/eform-imported.module';
import {
  DocumentsContainerComponent,
  DocumentsDocumentCreateComponent,
  DocumentsDocumentDeleteComponent,
  DocumentsDocumentEditComponent,
  DocumentsFiltersComponent,
  DocumentsFolderCreateComponent,
  DocumentsFolderEditComponent,
  DocumentsFolderDeleteComponent,
  DocumentsTableComponent,
  DocumentsFoldersComponent,
} from './components';
import {DocumentsRouting} from './documents.routing';
import {MatButtonModule} from '@angular/material/button';
import {MatTooltipModule} from '@angular/material/tooltip';
import {MatFormFieldModule} from '@angular/material/form-field';
import {MtxSelectModule} from '@ng-matero/extensions/select';
import {MatDialogModule} from '@angular/material/dialog';
import {MatIconModule} from '@angular/material/icon';
import {MatInputModule} from '@angular/material/input';
import {MtxGridModule} from '@ng-matero/extensions/grid';
import {MatCardModule} from '@angular/material/card';
import {MatSlideToggleModule} from '@angular/material/slide-toggle';
import {MatCheckboxModule} from '@angular/material/checkbox';
import {MtxCheckboxGroupModule} from '@ng-matero/extensions/checkbox-group';
import {MatDatepickerModule} from '@angular/material/datepicker';
import {StatisticsModule} from '../statistics/statistics.module';

@NgModule({
  declarations: [
    DocumentsContainerComponent,
    DocumentsTableComponent,
    DocumentsFiltersComponent,
    DocumentsFoldersComponent,
    DocumentsFolderCreateComponent,
    DocumentsFolderEditComponent,
    DocumentsFolderDeleteComponent,
    DocumentsDocumentCreateComponent,
    DocumentsDocumentEditComponent,
    DocumentsDocumentDeleteComponent
  ],
  imports: [
    CommonModule,
    TranslateModule,
    RouterModule,
    DocumentsRouting,
    EformSharedModule,
    ReactiveFormsModule,
    EformImportedModule,
    FormsModule,
    MatButtonModule,
    MatTooltipModule,
    MatIconModule,
    MatFormFieldModule,
    MtxSelectModule,
    MatInputModule,
    MtxGridModule,
    MatDialogModule,
    MatCardModule,
    MatSlideToggleModule,
    MatCheckboxModule,
    MtxCheckboxGroupModule,
    MatDatepickerModule,
    StatisticsModule,
  ],
  providers: [],
})

export class DocumentsModule {
}
