import {Component, EventEmitter, Inject, OnInit} from '@angular/core';
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
  fileTagsUpdated: EventEmitter<void> = new EventEmitter<void>();
  oldFileModel: FilesModel;
  currentTagIds: number[] = [];
  availableTags: SharedTagModel[] = [];

  get disabled() {
    return R.equals(this.oldFileModel.tags.map(x => x.id), this.currentTagIds);
  }

  constructor(
    private backendConfigurationPnFilesService: BackendConfigurationPnFilesService,
    public dialogRef: MatDialogRef<FileTagsEditComponent>,
    @Inject(MAT_DIALOG_DATA) model: { fileModel: FilesModel, availableTags: SharedTagModel[] }
  ) {
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
