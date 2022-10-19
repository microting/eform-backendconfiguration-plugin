import {
  Component, EventEmitter,
  OnDestroy,
  OnInit, Output,
  ViewChild,
} from '@angular/core';
import { AutoUnsubscribe } from 'ngx-auto-unsubscribe';
import {FolderDto, FolderModel, FolderUpdateModel, SharedTagModel} from 'src/app/common/models';
import {DocumentFolderModel} from 'src/app/plugins/modules/backend-configuration-pn/models';
import {BackendConfigurationPnDocumentsService} from 'src/app/plugins/modules/backend-configuration-pn/services';
import {ToastrService} from 'ngx-toastr';
import {TranslateService} from '@ngx-translate/core';
import {LocaleService} from 'src/app/common/services';
import {Subscription} from 'rxjs';
import {applicationLanguages, applicationLanguagesTranslated} from 'src/app/common/const';

@AutoUnsubscribe()
@Component({
  selector: 'app-documents-folder-edit',
  templateUrl: './documents-folder-edit.component.html',
  styleUrls: ['./documents-folder-edit.component.scss'],
})
export class DocumentsFolderEditComponent implements OnInit, OnDestroy {
  @ViewChild('frame') frame;
  name = '';
  selectedParentFolder: FolderDto;
  // newFolderModel: FolderCreateModel = new FolderCreateModel();
  // folderTranslations: FormArray = new FormArray([]);
  selectedLanguage: number;
  @Output() folderUpdate: EventEmitter<DocumentFolderModel> = new EventEmitter<
    DocumentFolderModel
  >();
  @Output() folderUpdateCancelled: EventEmitter<void> = new EventEmitter<void>();
  // tagModel: SharedTagModel = new SharedTagModel();
  folderUpdateModel: DocumentFolderModel = new DocumentFolderModel();
  updateFolderSub$: Subscription;
  getFolderSub$: Subscription;

  get languages() {
    return applicationLanguagesTranslated;
  }

  constructor(
    public backendConfigurationPnDocumentsService: BackendConfigurationPnDocumentsService,
    private toastrService: ToastrService,
    private translateService: TranslateService,
    localeService: LocaleService) {
    this.selectedLanguage = applicationLanguages.find(
      (x) => x.locale === localeService.getCurrentUserLocale()
    ).id;
  }

  ngOnInit() {}

  show(selectedFolder: DocumentFolderModel) {
    this.getFolder(selectedFolder.id);
    // this.folderUpdateModel = model;
    this.frame.show();
  }

  hide() {
    this.frame.hide();
  }

  updateItem() {
    // this.tagUpdate.emit(this.tagModel);
  }

  ngOnDestroy(): void {}

  cancelEdit() {
    this.frame.hide();
    this.folderUpdateCancelled.emit();
  }


  initEditForm(model: DocumentFolderModel) {
    this.folderUpdateModel = new DocumentFolderModel();
    this.folderUpdateModel = {
      ...this.folderUpdateModel,
      id: model.id,
      documentFolderTranslations: [],
    };
    for (let i = 0; i < applicationLanguagesTranslated.length; i++) {
      const translations = model.documentFolderTranslations.find(
        (x) => x.languageId === applicationLanguagesTranslated[i].id
      );
      this.folderUpdateModel.documentFolderTranslations.push({
        languageId: applicationLanguagesTranslated[i].id,
        description: translations ? translations.description : '',
        name: translations ? translations.name : '',
        id: translations.id
      });
    }
  }


  updateFolder() {
    // Validate if at least one translation is filled correctly
    const translationExists = this.folderUpdateModel.documentFolderTranslations.find(
      (x) => x.name
    );

    if (translationExists) {
      this.updateFolderSub$ = this.backendConfigurationPnDocumentsService
        .updateFolder(this.folderUpdateModel)
        .subscribe((operation) => {
          if (operation && operation.success) {
            // this.folderEdited.emit();
            this.frame.hide();
            this.folderUpdate.emit(this.folderUpdateModel);
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
