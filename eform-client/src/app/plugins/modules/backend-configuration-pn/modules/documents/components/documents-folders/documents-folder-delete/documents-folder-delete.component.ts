import {Component, EventEmitter, Inject, OnInit,} from '@angular/core';
import {MAT_DIALOG_DATA, MatDialogRef} from '@angular/material/dialog';
import {AuthStateService} from 'src/app/common/store';
import {applicationLanguages} from 'src/app/common/const';
import {BackendConfigurationPnDocumentsService} from '../../../../../services';
import {DocumentFolderModel} from '../../../../../models';

@Component({
  selector: 'app-folder-delete',
  templateUrl: './documents-folder-delete.component.html',
  styleUrls: ['./documents-folder-delete.component.scss']
})
export class DocumentsFolderDeleteComponent implements OnInit {
  folderDelete: EventEmitter<void> = new EventEmitter<void>();

  constructor(
    private backendConfigurationPnDocumentsService: BackendConfigurationPnDocumentsService,
    private authStateService: AuthStateService,
    public dialogRef: MatDialogRef<DocumentsFolderDeleteComponent>,
    @Inject(MAT_DIALOG_DATA) public folder: DocumentFolderModel,
  ) {
  }

  ngOnInit() {
  }


  hide(result = false) {
    this.dialogRef.close(result);
  }

  deleteFolder() {
    this.backendConfigurationPnDocumentsService.deleteFolder(this.folder.id)
      .subscribe(operation => {
        if (operation && operation.success) {
          this.folderDelete.emit();
        }
      });
  }

  get nameFolder() {
    if (this.folder) {
      const languageId = applicationLanguages.find(x => x.locale === this.authStateService.currentUserLocale).id;
      const filteredTranslations = this.folder.documentFolderTranslations.filter(x => x.languageId === languageId);
      if (filteredTranslations.length) {
        return filteredTranslations[0].name;
      }
    }
    return '';
  }

  get descriptionFolder() {
    if (this.folder) {
      const languageId = applicationLanguages.find(x => x.locale === this.authStateService.currentUserLocale).id;
      const filteredTranslations = this.folder.documentFolderTranslations.filter(x => x.languageId === languageId);
      if (filteredTranslations.length) {
        return filteredTranslations[0].description;
      }
    }
    return '';
  }
}
