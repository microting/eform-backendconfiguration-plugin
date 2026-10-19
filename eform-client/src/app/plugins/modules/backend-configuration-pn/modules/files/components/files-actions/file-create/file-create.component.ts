import {Component, OnDestroy, OnInit, ViewChild,
  inject
} from '@angular/core';
import {FilesCreateModel} from '../../../../../models';
import {SharedTagModel} from 'src/app/common/models';
import {
  BackendConfigurationPnFilesService,
  BackendConfigurationPnArchiveFoldersService,
  BackendConfigurationPnFileTagsService,
  BackendConfigurationPnPropertiesService
} from '../../../../../services';
import {Subscription, take} from 'rxjs';
import {Store} from '@ngrx/store';
import {ArchiveFolderOption, folderOptions} from '../../../../../helpers';
import {selectFilesFolderId} from '../../../../../state';
import {FileTagsComponent} from '../../file-tags/file-tags.component';
import {AutoUnsubscribe} from 'ngx-auto-unsubscribe';
import * as R from 'ramda';
import {ActivatedRoute, Router, RouterLink} from '@angular/router';
import {NgIf, NgFor} from '@angular/common';
import {TranslatePipe} from '@ngx-translate/core';
import {MatIcon} from '@angular/material/icon';
import {MatIconButton} from '@angular/material/button';
import {MatCard, MatCardContent, MatCardTitle, MatCardFooter} from '@angular/material/card';
import {MatFormField, MatLabel} from '@angular/material/form-field';
import {MtxSelect} from '@ng-matero/extensions/select';
import {MatTooltip} from '@angular/material/tooltip';
import {EformNewSubheaderComponent} from 'src/app/common/modules/eform-shared/components';
import {FileCreateDropZoneComponent} from './file-create-drop-zone/file-create-drop-zone.component';
import {FileCreateEditFileComponent} from './file-create-edit-file/file-create-edit-file.component';
import {ArchiveFolderPickerComponent} from '../../archive-folder-picker/archive-folder-picker.component';
import {PdfViewerModule} from 'ng2-pdf-viewer';

@AutoUnsubscribe()
@Component({
    selector: 'app-files-file-create',
    imports: [NgIf, NgFor, TranslatePipe, RouterLink, MatIcon, MatIconButton, MatCard, MatCardContent, MatCardTitle, MatFormField, MatLabel, MtxSelect, MatTooltip, EformNewSubheaderComponent, FileCreateDropZoneComponent, FileCreateEditFileComponent, ArchiveFolderPickerComponent, FileTagsComponent, MatCardFooter, PdfViewerModule],
    templateUrl: './file-create.component.html',
    styleUrls: ['./file-create.component.scss'],
})
export class FileCreateComponent implements OnInit, OnDestroy {
  private propertiesService = inject(BackendConfigurationPnPropertiesService);
  private backendConfigurationPnFilesService = inject(BackendConfigurationPnFilesService);
  private tagsService = inject(BackendConfigurationPnFileTagsService);
  private router = inject(Router);
  private route = inject(ActivatedRoute);
  private foldersService = inject(BackendConfigurationPnArchiveFoldersService);
  private store = inject(Store);
  folderOptions: ArchiveFolderOption[] = [];
  /** Required: every archive document lies in exactly one folder (D1). */
  selectedFolderId: number | null = null;
  getFoldersSub$: Subscription;

  @ViewChild('tagsModal') tagsModal: FileTagsComponent;

  mimePdfType = 'application/pdf';
  files: FilesCreateModel[] = [];
  getAllPropertiesSub$: any;
  availableProperties: { name: string; id: number }[];
  selectedProperties: number[];
  selectedTags: number[] = [];
  selectedFile: FilesCreateModel = null;
  getTagsSub$: Subscription;

  get availableTags(): SharedTagModel[] {
    return this._availableTags;
  }

  get disabledUploadBtn(): boolean {
    // disabled if no files, no property or no folder
    return this.files.length === 0 || !this.selectedProperties || !this.selectedFolderId;
  }

  set availableTags(val: SharedTagModel[]) {
    this._availableTags = val ?? [];
    if (this.selectedTags) {
      // delete from selector deleted tags
      const newTagIdsWithoutDeletedTags = this.selectedTags.filter((x: number) => this._availableTags.some(y => y.id === x));
      if (newTagIdsWithoutDeletedTags.length !== this.selectedTags.length) {
        this.selectedTags = newTagIdsWithoutDeletedTags;
      }
    }
  }

  private _availableTags: SharedTagModel[] = [];

  get filesSelected(): boolean {
    if (this.files.length > 0) {
      return true;
    }
    return false;
  }

  

  ngOnInit(): void {
    this.getProperties();
    this.getTags();
    this.getFolders();
  }

  getProperties() {
    this.getAllPropertiesSub$ = this.propertiesService.getAllPropertiesDictionary()
      .subscribe((data) => {
        if (data && data.success && data.model) {
          this.availableProperties = data.model;
        }
      });
  }

  getTags() {
    this.getTagsSub$ = this.tagsService.getTags().subscribe((data) => {
      if (data && data.success) {
        this.availableTags = data.model;
      }
    });
  }

  /** Preselects the folder the Arkiv page showed (?folderId=), else the store's folder, else the default folder. */
  getFolders() {
    const requested = Number(this.route.snapshot.queryParamMap.get('folderId'));
    this.store.select(selectFilesFolderId).pipe(take(1)).subscribe(current => {
      this.getFoldersSub$ = this.foldersService.getFolders([]).subscribe(res => {
        if (!res?.success || !res.model) {
          return;
        }
        const folders = res.model;
        const known = (id: number | null) => id !== null && folders.some(f => f.id === id);
        this.folderOptions = folderOptions(folders);
        if (known(requested)) {
          this.selectedFolderId = requested;
        } else if (known(current)) {
          this.selectedFolderId = current;
        } else {
          this.selectedFolderId = folders.find(f => f.isDefault)?.id ?? null;
        }
      });
    });
  }

  openTagsModal() {
    this.tagsModal.show();
  }

  onFilesChanged(files: File[]) {
    this.files = [...this.files, ...files
      .filter(x => !this.files.some(y => y.file.name === x.name))
      .map((file): FilesCreateModel => {
        let mappedFile: FilesCreateModel = {
          src: undefined,
          file: file,
          propertyIds: this.selectedProperties,
          tagIds: [...this.selectedTags]
        };
        file.arrayBuffer().then(src => mappedFile.src = new Uint8Array(src));
        return mappedFile;
      })]
      .sort((a, b) => a.file.name < b.file.name ? -1 : a.file.name > b.file.name ? 1 : 0);
  }

  deleteFile(file: FilesCreateModel) {
    this.files = this.files.filter(x => x.file.name !== file.file.name);
  }

  ngOnDestroy(): void {
  }

  editFile(file: FilesCreateModel) {
    file.file.arrayBuffer().then(arrayBuffer => {
      debugger;
      this.selectedFile = new FilesCreateModel();
      this.selectedFile = {
        file: new File([arrayBuffer], file.file.name),
        propertyIds: file.propertyIds,
        src: new Uint8Array(arrayBuffer),
        tagIds: file.tagIds
      }
      //{...file, file: new File([arrayBuffer], file.file.name), src: new Uint8Array(arrayBuffer)};
      //this.selectedFile = {...file, file: new File([arrayBuffer], file.file.name), src: new Uint8Array(arrayBuffer)};
    })
  }

  onSaveEditedFile(file: FilesCreateModel) {
    const i = this.files.findIndex(x => x.file.name === file.file.name);
    if (i !== -1) {
      this.files[i] = file;
      this.selectedFile = null;
    }
  }

  selectedPropertyChange(selectedProperty: { name: string; id: number }[]) {
    const propertyIds = selectedProperty.map(x => x.id);
    this.files.forEach(x => x.propertyIds = [...propertyIds]);
    this.selectedProperties = [...propertyIds];
  }

  selectedTagsChange(tags: SharedTagModel[]) {
    const tagIds = tags.map(x => x.id);
    // change tags for all files
    this.files.forEach(x => {
      if (R.difference(x.tagIds, tagIds).length === 0) {
        x.tagIds = tagIds;
      }
    });
    this.selectedTags = tagIds;
  }

  uploadFiles() {
    let model = this.files.map((x) => ({
      file: x.file,
      propertyIds: x.propertyIds,
      tagIds: x.tagIds,
      folderId: this.selectedFolderId,
    }));
    this.backendConfigurationPnFilesService
      .createFiles({filesForCreate: [...model]})
      .subscribe(operationResult => {
        if(operationResult && operationResult.success){
          this.router.navigate([`..`], {relativeTo: this.route}).then();
        }
      });
  }
}
