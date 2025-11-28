import {Component, OnDestroy, OnInit, ViewChild, inject} from '@angular/core';
import {FilesCreateModel} from '../../../../../models';
import {SharedTagModel} from 'src/app/common/models';
import {
  BackendConfigurationPnFilesService,
  BackendConfigurationPnFileTagsService,
  BackendConfigurationPnPropertiesService
} from '../../../../../services';
import {Subscription} from 'rxjs';
import {FileTagsComponent} from '../../';
import {AutoUnsubscribe} from 'ngx-auto-unsubscribe';
import * as R from 'ramda';
import {ActivatedRoute, Router} from '@angular/router';

@AutoUnsubscribe()
@Component({
    selector: 'app-files-file-create',
    templateUrl: './file-create.component.html',
    styleUrls: ['./file-create.component.scss'],
    standalone: false
})
export class FileCreateComponent implements OnInit, OnDestroy {
  private propertiesService = inject(BackendConfigurationPnPropertiesService);
  private backendConfigurationPnFilesService = inject(BackendConfigurationPnFilesService);
  private tagsService = inject(BackendConfigurationPnFileTagsService);
  private router = inject(Router);
  private route = inject(ActivatedRoute);

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
    // disabled if not files and not set property for all files
    return this.files.length === 0 || /*(*/!this.selectedProperties/* || this.files.findIndex(x => !!x.propertyId) === -1)*/;
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
      tagIds: x.tagIds
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
