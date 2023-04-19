import {NgModule} from '@angular/core';
import {CommonModule} from '@angular/common';
import {TranslateModule} from '@ngx-translate/core';
import {RouterModule} from '@angular/router';
import {OWL_DATE_TIME_FORMATS, OwlDateTimeModule} from '@danielmoncada/angular-datetime-picker';
import {EformSharedModule} from 'src/app/common/modules/eform-shared/eform-shared.module';
import {FormsModule, ReactiveFormsModule} from '@angular/forms';
import {NgSelectModule} from '@ng-select/ng-select';
import {EformImportedModule} from 'src/app/common/modules/eform-imported/eform-imported.module';
import {MY_MOMENT_FORMATS_FOR_TASK_MANAGEMENT} from '../../consts';
import {
  DocumentsContainerComponent,
  DocumentsDocumentCreateComponent,
  DocumentsDocumentDeleteComponent,
  DocumentsDocumentEditComponent,
  DocumentsFiltersComponent,
  DocumentsFolderCreateComponent,
  DocumentsFolderEditComponent,
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

@NgModule({
  declarations: [
    DocumentsContainerComponent,
    DocumentsTableComponent,
    DocumentsFiltersComponent,
    DocumentsFoldersComponent,
    DocumentsFolderCreateComponent,
    DocumentsFolderEditComponent,
    DocumentsDocumentCreateComponent,
    DocumentsDocumentEditComponent,
    DocumentsDocumentDeleteComponent
  ],
  imports: [
    CommonModule,
    TranslateModule,
    RouterModule,
    OwlDateTimeModule,
    DocumentsRouting,
    OwlDateTimeModule,
    EformSharedModule,
    ReactiveFormsModule,
    NgSelectModule,
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
  ],
  providers: [
    {
      provide: OWL_DATE_TIME_FORMATS,
      useValue: MY_MOMENT_FORMATS_FOR_TASK_MANAGEMENT,
    },
  ],
})

export class DocumentsModule {}
