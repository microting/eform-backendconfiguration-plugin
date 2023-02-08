import {Component, EventEmitter, Inject, OnInit} from '@angular/core';
import {FilesModel} from '../../../../../models';
import {BackendConfigurationPnFilesService} from '../../../../../services';
import {MAT_DIALOG_DATA, MatDialogRef} from '@angular/material/dialog';

@Component({
  selector: 'app-file-name-edit',
  templateUrl: './file-name-edit.component.html',
  styleUrls: ['./file-name-edit.component.scss']
})
export class FileNameEditComponent implements OnInit {
  fileNameUpdated: EventEmitter<void> = new EventEmitter<void>();
  public oldFileModel: FilesModel;

  constructor(
    private backendConfigurationPnFilesService: BackendConfigurationPnFilesService,
    public dialogRef: MatDialogRef<FileNameEditComponent>,
    @Inject(MAT_DIALOG_DATA) public fileModel: FilesModel,
  ) {
    this.oldFileModel = {...fileModel};
  }

  ngOnInit(): void {
  }

  hide() {
    this.dialogRef.close();
  }

  updateFile() {
    this.backendConfigurationPnFilesService.updateFileName({newName: this.fileModel.fileName, id: this.fileModel.id})
      .subscribe((data) => {
        if (data && data.success) {
          this.fileNameUpdated.emit();
          this.hide();
        }
      });
  }
}
