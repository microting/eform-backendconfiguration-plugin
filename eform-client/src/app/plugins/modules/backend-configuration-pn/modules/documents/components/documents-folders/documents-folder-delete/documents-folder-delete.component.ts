import {Component, EventEmitter, OnInit, inject} from '@angular/core';
import {MAT_DIALOG_DATA, MatDialogRef} from '@angular/material/dialog';
import {AuthStateService} from 'src/app/common/store';
import {applicationLanguages} from 'src/app/common/const';
import {BackendConfigurationPnDocumentsService} from '../../../../../services';
import {DocumentFolderModel} from '../../../../../models';
import {selectCurrentUserLanguageId} from 'src/app/state/auth/auth.selector';
import {Store} from '@ngrx/store';

@Component({
    selector: 'app-folder-delete',
    templateUrl: './documents-folder-delete.component.html',
    styleUrls: ['./documents-folder-delete.component.scss'],
    standalone: false
})
export class DocumentsFolderDeleteComponent implements OnInit {
  private authStore = inject(Store);
  private backendConfigurationPnDocumentsService = inject(BackendConfigurationPnDocumentsService);
  private authStateService = inject(AuthStateService);
  public dialogRef = inject(MatDialogRef<DocumentsFolderDeleteComponent>);
  public folder = inject<DocumentFolderModel>(MAT_DIALOG_DATA);

  folderDelete: EventEmitter<void> = new EventEmitter<void>();
  private selectCurrentUserLanguageId$ = this.authStore.select(selectCurrentUserLanguageId);

  

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
      let languageId = 0;
      this.selectCurrentUserLanguageId$.subscribe((x) => {
        languageId = x;
      });
      // const languageId = applicationLanguages.find(x => x.locale === this.authStateService.currentUserLocale).id;
      const filteredTranslations = this.folder.documentFolderTranslations.filter(x => x.languageId === languageId);
      if (filteredTranslations.length) {
        return filteredTranslations[0].name;
      }
    }
    return '';
  }

  get descriptionFolder() {
    if (this.folder) {
      let languageId = 0;
      this.selectCurrentUserLanguageId$.subscribe((x) => {
        languageId = x;
      });
      const filteredTranslations = this.folder.documentFolderTranslations.filter(x => x.languageId === languageId);
      if (filteredTranslations.length) {
        return filteredTranslations[0].description;
      }
    }
    return '';
  }
}
