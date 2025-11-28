import {Component, EventEmitter, OnInit,
  inject
} from '@angular/core';
import {FilesModel} from '../../../../../models';
import {BackendConfigurationPnFilesService} from '../../../../../services';
import {MAT_DIALOG_DATA, MatDialogRef} from '@angular/material/dialog';
import {SharedTagModel} from 'src/app/common/models';
import * as R from 'ramda';

@Component({
    selector: 'app-file-tags-edit',
    templateUrl: './file-tags-edit.component.html',
    styleUrls: ['./file-tags-edit.component.scss'],
    standalone: false
})
export class FileTagsEditComponent implements OnInit {
  private backendConfigurationPnFilesService = inject(BackendConfigurationPnFilesService);
  public dialogRef = inject(MatDialogRef<FileTagsEditComponent>);
  private model = inject<{ fileModel: FilesModel, availableTags: SharedTagModel[] }>(MAT_DIALOG_DATA);

  fileTagsUpdated: EventEmitter<void> = new EventEmitter<void>();
  oldFileModel: FilesModel;
  currentTagIds: number[] = [];
  availableTags: SharedTagModel[] = [];

  get disabled() {
    return R.equals(this.oldFileModel.tags.map(x => x.id), this.currentTagIds);
  }

  
  constructor() {
    this.oldFileModel = {...model.fileModel};
    this.currentTagIds = model.fileModel.tags.map(x => x.id);
    this.availableTags = model.availableTags;
  }


  ngOnInit(): void {
  }

  hide() {
    this.dialogRef.close();
  }

  updateFile() {
    this.backendConfigurationPnFilesService.updateFileTags({tags: this.currentTagIds, id: this.oldFileModel.id})
      .subscribe((data) => {
        if (data && data.success) {
          this.fileTagsUpdated.emit();
          this.hide();
        }
      });
  }
}
