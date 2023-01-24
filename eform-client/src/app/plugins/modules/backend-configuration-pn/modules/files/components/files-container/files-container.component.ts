import {Component, OnDestroy, OnInit, ViewChild} from '@angular/core';
import {FileNameEditComponent, FileTagsEditComponent} from '../';
import {FilesModel} from '../../../../models';
import {
  DeleteModalSettingModel,
  Paged,
  SharedTagModel
} from 'src/app/common/models';
import {Subscription, zip} from 'rxjs';
import {FilesStateService} from '../../store';
import {MatDialog} from '@angular/material/dialog';
import {Overlay} from '@angular/cdk/overlay';
import {dialogConfigHelper} from 'src/app/common/helpers';
import {BackendConfigurationPnFilesService, BackendConfigurationPnFileTagsService} from '../../../../services';
import {EformsTagsComponent} from 'src/app/common/modules/eform-shared-tags/components';
import {DeleteModalComponent} from 'src/app/common/modules/eform-shared/components';
import {TranslateService} from '@ngx-translate/core';

@Component({
  selector: 'app-files-container',
  templateUrl: './files-container.component.html',
  styleUrls: ['./files-container.component.scss'],
})
export class FilesContainerComponent implements OnInit, OnDestroy {
  @ViewChild('tagsModal') tagsModal: EformsTagsComponent;
  availableTags: SharedTagModel[] = [];
  files: Paged<FilesModel> = new Paged<FilesModel>();

  getTagsSub$: Subscription;
  filesDeletedSub$: Subscription;
  fileNameUpdatedSub$: Subscription;
  translatesSub$: Subscription;
  fileTagsUpdatedSub$: Subscription;

  constructor(
    public dialog: MatDialog,
    private overlay: Overlay,
    public filesStateService: FilesStateService,
    private tagsService: BackendConfigurationPnFileTagsService,
    private translateService: TranslateService,
    private filesService: BackendConfigurationPnFilesService,
  ) {
  }

  ngOnInit(): void {
    this.getTags();
    this.getFiles();
    // this.filesStateService.getFiltersAsync().subscribe(() => this.updateTable());
  }

  ngOnDestroy(): void {
  }

  showEditModal(file: FilesModel) {
    this.filesService.getFile(file.id).subscribe(model => {
      if(model && model.success && model.model) {
        const editFileModal = this.dialog.open(FileNameEditComponent, {...dialogConfigHelper(this.overlay, model.model)});
        this.fileNameUpdatedSub$ = editFileModal.componentInstance.fileNameUpdated.subscribe(() => {
          this.updateTable();
        });
      }
    })
  }

  showDeleteModal(file: FilesModel) {
    this.translatesSub$ = zip(
      this.translateService.stream('Delete file'),
      this.translateService.stream('File name'),
    ).subscribe(([headerText, fileName]) => {
      const settings: DeleteModalSettingModel = {
        model: file,
        settings: {
          headerText: `${headerText}?`,
          fields: [
            {header: fileName, field: 'fileName'},
          ],
          cancelButtonId: 'cancelDeleteFileBtn',
          deleteButtonId: 'deleteFileBtn',
        }
      };
      const fileDeleteModal = this.dialog.open(DeleteModalComponent, {...dialogConfigHelper(this.overlay, settings)});
      this.filesDeletedSub$ = fileDeleteModal.componentInstance.delete
        .subscribe((model: FilesModel) => {
          this.filesService.deleteFile(model.id)
            .subscribe(operation => {
              if (operation && operation.success) {
                fileDeleteModal.close();
                this.updateTable();
              }
            });
        });
    });
  }

  updateTable() {
    this.getFiles();
  }

  getFiles() {
    this.filesStateService.getFiles()
      .subscribe(model => {
        if(model && model.success && model.model) {
          this.files = model.model;
        }
      })
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

  addTagToFilter(tagId: number) {
    const filters = this.filesStateService.store.getValue().filters;
    if(!filters.tagIds.some(x => x === tagId)) {
      filters.tagIds = [...filters.tagIds, tagId];
      this.filesStateService.updateFilters(filters)
      this.updateTable();
    }
  }

  showEditTagsModal(model: FilesModel) {
    this.filesService.getFile(model.id).subscribe(model => {
      if(model && model.success && model.model) {
        const editFileTagsModal = this.dialog.open(FileTagsEditComponent,
          {...dialogConfigHelper(this.overlay, {fileModel: model.model, availableTags: this.availableTags})});
        this.fileTagsUpdatedSub$ = editFileTagsModal.componentInstance.fileTagsUpdated.subscribe(() => {
          this.updateTable();
        });
      }
    })
  }

  showFile(fileId: number) {

  }
}
