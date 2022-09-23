import {NgModule} from '@angular/core';
import {CommonModule} from '@angular/common';
import {MDBBootstrapModule} from 'angular-bootstrap-md';
import {TranslateModule} from '@ngx-translate/core';
import {RouterModule} from '@angular/router';
import {OWL_DATE_TIME_FORMATS, OwlDateTimeModule, OwlMomentDateTimeModule} from '@danielmoncada/angular-datetime-picker';
import {EformSharedModule} from 'src/app/common/modules/eform-shared/eform-shared.module';
import {FontAwesomeModule} from '@fortawesome/angular-fontawesome';
import {FormsModule, ReactiveFormsModule} from '@angular/forms';
import {NgSelectModule} from '@ng-select/ng-select';
import {EformImportedModule} from 'src/app/common/modules/eform-imported/eform-imported.module';
import {MY_MOMENT_FORMATS_FOR_TASK_MANAGEMENT} from 'src/app/plugins/modules/backend-configuration-pn/consts';
import {
  DocumentsContainerComponent, DocumentsDocumentCreateComponent, DocumentsDocumentDeleteComponent, DocumentsDocumentEditComponent,
  DocumentsFiltersComponent, DocumentsFolderCreateComponent, DocumentsFolderEditComponent
} from 'src/app/plugins/modules/backend-configuration-pn/modules/documents/components';
import {DocumentsRouting} from 'src/app/plugins/modules/backend-configuration-pn/modules/documents/documents.routing';
import {
  DocumentsTableComponent
} from 'src/app/plugins/modules/backend-configuration-pn/modules/documents/components/documents-table/documents-table.component';
import {
  DocumentsFoldersComponent
} from 'src/app/plugins/modules/backend-configuration-pn/modules/documents/components/documents-folders/documents-folders/documents-folders.component';

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
    MDBBootstrapModule,
    TranslateModule,
    RouterModule,
    OwlDateTimeModule,
    DocumentsRouting,
    OwlDateTimeModule,
    OwlMomentDateTimeModule,
    EformSharedModule,
    FontAwesomeModule,
    ReactiveFormsModule,
    NgSelectModule,
    EformImportedModule,
    FormsModule,
  ],
  providers: [
    {
      provide: OWL_DATE_TIME_FORMATS,
      useValue: MY_MOMENT_FORMATS_FOR_TASK_MANAGEMENT,
    },
  ],
})

export class DocumentsModule {}
