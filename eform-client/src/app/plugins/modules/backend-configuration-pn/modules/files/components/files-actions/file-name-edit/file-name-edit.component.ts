import {Component, EventEmitter, Inject, OnDestroy, OnInit} from '@angular/core';
import {FileModel} from '../../../../../models';
import {BackendConfigurationPnFilesService, BackendConfigurationPnPropertiesService} from '../../../../../services';
import {MAT_DIALOG_DATA, MatDialogRef} from '@angular/material/dialog';
import * as R from 'ramda'
import { CommonDictionaryModel } from 'src/app/common/models';
import {Subscription} from 'rxjs';
import {AutoUnsubscribe} from 'ngx-auto-unsubscribe';

@AutoUnsubscribe()
@Component({
  selector: 'app-file-name-edit',
  templateUrl: './file-name-edit.component.html',
  styleUrls: ['./file-name-edit.component.scss']
})
export class FileNameEditComponent implements OnInit, OnDestroy {
  fileNameUpdated: EventEmitter<void> = new EventEmitter<void>();
  public oldFileModel: FileModel;
  public availableProperties: CommonDictionaryModel[] = [];

  getAllPropertiesSub$: Subscription;

  constructor(
    private backendConfigurationPnFilesService: BackendConfigurationPnFilesService,
    private backendConfigurationPnPropertiesService: BackendConfigurationPnPropertiesService,
    public dialogRef: MatDialogRef<FileNameEditComponent>,
    @Inject(MAT_DIALOG_DATA) public fileModel: FileModel,
  ) {
    this.oldFileModel = {...fileModel};
    this.getAllPropertiesSub$ = this.backendConfigurationPnPropertiesService.getAllPropertiesDictionary()
      .subscribe((data) => {
        if (data && data.success && data.model) {
          this.availableProperties = data.model;
        }
      });
  }

  get disabledSaveBtn() {
    return R.equals(
      {
        name: this.oldFileModel.fileName,
        properties: this.oldFileModel.properties,
      },
      {
        name: this.fileModel.fileName,
        properties: this.fileModel.properties
      }) || this.fileModel.properties.length === 0;
  }

  ngOnInit(): void {
  }

  hide() {
    this.dialogRef.close();
  }

  updateFile() {
    this.backendConfigurationPnFilesService
      .updateFileName({newName: this.fileModel.fileName, id: this.fileModel.id, propertyIds: this.fileModel.properties})
      .subscribe((data) => {
        if (data && data.success) {
          this.fileNameUpdated.emit();
          this.hide();
        }
      });
  }

  ngOnDestroy(): void {
  }

  changeSelectedProperties(properties: CommonDictionaryModel[]) {
    this.fileModel.properties = properties.map(x => x.id);
  }
}
