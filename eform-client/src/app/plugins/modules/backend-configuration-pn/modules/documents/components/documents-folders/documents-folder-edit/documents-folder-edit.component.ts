import {
  Component,
  EventEmitter,
  Inject,
  OnDestroy,
  OnInit,
} from '@angular/core';
import { AutoUnsubscribe } from 'ngx-auto-unsubscribe';
import {FolderDto,} from 'src/app/common/models';
import {DocumentFolderModel} from '../../../../../models';
import {BackendConfigurationPnDocumentsService} from '../../../../../services';
import {ToastrService} from 'ngx-toastr';
import {TranslateService} from '@ngx-translate/core';
import {LocaleService} from 'src/app/common/services';
import {Subscription} from 'rxjs';
import {applicationLanguages2 } from 'src/app/common/const';
import {MAT_DIALOG_DATA, MatDialogRef} from '@angular/material/dialog';
import {selectCurrentUserLanguageId} from 'src/app/state/auth/auth.selector';
import {Store} from '@ngrx/store';

@AutoUnsubscribe()
@Component({
    selector: 'app-documents-folder-edit',
    templateUrl: './documents-folder-edit.component.html',
    styleUrls: ['./documents-folder-edit.component.scss'],
    standalone: false
})
export class DocumentsFolderEditComponent implements OnInit, OnDestroy {
  name = '';
  selectedParentFolder: FolderDto;
  selectedLanguageId: number;
  folderUpdate: EventEmitter<void> = new EventEmitter<void>();
  folderUpdateModel: DocumentFolderModel = new DocumentFolderModel();
  updateFolderSub$: Subscription;
  getFolderSub$: Subscription;
  private selectCurrentUserLanguageId$ = this.authStore.select(selectCurrentUserLanguageId);

  get languages() {
    return applicationLanguages2;
  }

  constructor(
    private authStore: Store,
    public backendConfigurationPnDocumentsService: BackendConfigurationPnDocumentsService,
    private toastrService: ToastrService,
    private translateService: TranslateService,
    localeService: LocaleService,
    public dialogRef: MatDialogRef<DocumentsFolderEditComponent>,
    @Inject(MAT_DIALOG_DATA) selectedFolder: DocumentFolderModel,
  ) {
    this.getFolder(selectedFolder.id);

    this.selectCurrentUserLanguageId$.subscribe((languageId) => {
      this.selectedLanguageId = languageId;
    });
    // this.selectedLanguage = applicationLanguages2.find(
    //   (x) => x.locale === localeService.getCurrentUserLocale()
    // ).id;
  }

  ngOnInit() {
  }

  hide() {
    this.dialogRef.close();
  }

  ngOnDestroy(): void {}

  initEditForm(model: DocumentFolderModel) {
    this.folderUpdateModel = new DocumentFolderModel();
    this.folderUpdateModel = {
      ...this.folderUpdateModel,
      id: model.id,
      documentFolderTranslations: [],
    };
    for (let i = 0; i < applicationLanguages2.length; i++) {
      const translations = model.documentFolderTranslations.find(
        (x) => x.languageId === applicationLanguages2[i].id
      );
      this.folderUpdateModel.documentFolderTranslations.push({
        languageId: applicationLanguages2[i].id,
        description: translations ? translations.description : '',
        name: translations ? translations.name : '',
        id: translations.id
      });
    }
  }


  updateFolder() {
    // Validate if at least one translation is filled correctly
    if (this.folderUpdateModel.documentFolderTranslations.some(x => x.name)) {
      this.updateFolderSub$ = this.backendConfigurationPnDocumentsService
        .updateFolder(this.folderUpdateModel)
        .subscribe((operation) => {
          if (operation && operation.success) {
            this.folderUpdate.emit();
          }
        });
    } else {
      this.toastrService.error(
        this.translateService.instant(
          'Folder translations should have at least one name/description pair'
        )
      );
      return;
    }
  }


  getFolder(folderId: number) {
    this.getFolderSub$ = this.backendConfigurationPnDocumentsService
      .getSingleFolder(folderId)
      .subscribe((data) => {
        if (data && data.success) {
          this.initEditForm(data.model);
        }
      });
  }

}
