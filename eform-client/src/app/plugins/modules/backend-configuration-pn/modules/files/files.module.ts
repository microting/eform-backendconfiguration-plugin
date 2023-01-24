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
import {MY_MOMENT_FORMATS_FOR_BACKEND_CONFIGURATIONS_PLUGIN} from '../../consts';
import {
  FilesContainerComponent,
  DocumentsDocumentCreateComponent,
  FileNameEditComponent,
  FilesFiltersComponent,
  FilesTableComponent,
  FileTagsComponent,
  FileTagsEditComponent,
} from './components';
import {FilesRouting} from './files.routing';
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
import {EformSharedTagsModule} from 'src/app/common/modules/eform-shared-tags/eform-shared-tags.module';
import {filesPersistProvider} from './store';
import {MatChipsModule} from '@angular/material/chips';

@NgModule({
  declarations: [
    FilesContainerComponent,
    FilesTableComponent,
    FilesFiltersComponent,
    DocumentsDocumentCreateComponent,
    FileNameEditComponent,
    FileTagsComponent,
    FileTagsEditComponent,
  ],
  imports: [
    CommonModule,
    MDBBootstrapModule,
    TranslateModule,
    RouterModule,
    OwlDateTimeModule,
    FilesRouting,
    OwlDateTimeModule,
    OwlMomentDateTimeModule,
    EformSharedModule,
    FontAwesomeModule,
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
    EformSharedTagsModule,
    MatChipsModule,
  ],
  providers: [
    {
      provide: OWL_DATE_TIME_FORMATS,
      useValue: MY_MOMENT_FORMATS_FOR_BACKEND_CONFIGURATIONS_PLUGIN,
    },
    filesPersistProvider,
  ],
})

export class FilesModule {}