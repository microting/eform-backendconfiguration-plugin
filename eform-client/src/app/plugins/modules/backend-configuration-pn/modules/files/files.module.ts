import {NgModule} from '@angular/core';
import {CommonModule} from '@angular/common';
import {TranslateModule} from '@ngx-translate/core';
import {RouterModule} from '@angular/router';
import {EformSharedModule} from 'src/app/common/modules/eform-shared/eform-shared.module';
import {FormsModule, ReactiveFormsModule} from '@angular/forms';
import {EformImportedModule} from 'src/app/common/modules/eform-imported/eform-imported.module';
import {
  FilesContainerComponent,
  FileCreateComponent,
  FileNameEditComponent,
  FilesFiltersComponent,
  FilesTableComponent,
  FileTagsComponent,
  FileTagsEditComponent,
  FileCreateDropZoneComponent,
  FileCreateEditFileComponent,
  DownloadFilesNameArchiveComponent,
  FileCreateZoomPageComponent,
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
import {MatChipsModule} from '@angular/material/chips';
import {PdfViewerModule} from 'ng2-pdf-viewer';
import {DragulaModule} from 'ng2-dragula';
import {MtxProgressModule} from '@ng-matero/extensions/progress';
import {MatDatepickerModule} from '@angular/material/datepicker';
import {MatMenu, MatMenuItem, MatMenuTrigger} from "@angular/material/menu";

@NgModule({
  declarations: [
    FilesContainerComponent,
    FilesTableComponent,
    FilesFiltersComponent,
    FileCreateComponent,
    FileNameEditComponent,
    FileTagsComponent,
    FileTagsEditComponent,
    FileCreateDropZoneComponent,
    FileCreateEditFileComponent,
    DownloadFilesNameArchiveComponent,
    FileCreateZoomPageComponent,
  ],
  imports: [
    CommonModule,
    TranslateModule,
    RouterModule,
    FilesRouting,
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
    EformSharedTagsModule,
    MatChipsModule,
    PdfViewerModule,
    DragulaModule,
    MtxProgressModule,
    MatDatepickerModule,
    MatMenuTrigger,
    MatMenu,
    MatMenuItem,
  ],
})

export class FilesModule {
}
