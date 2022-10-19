import {
  Component,
  EventEmitter,
  OnInit,
  Output,
  ViewChild,
} from '@angular/core';
import {FolderCreateModel, FolderDto, SharedTagCreateModel} from 'src/app/common/models';
import {applicationLanguages, applicationLanguagesTranslated} from 'src/app/common/const';
import {FoldersService, LocaleService} from 'src/app/common/services';
import {ToastrService} from 'ngx-toastr';
import {TranslateService} from '@ngx-translate/core';
import {BackendConfigurationPnDocumentsService} from 'src/app/plugins/modules/backend-configuration-pn/services';
import {DocumentFolderModel} from 'src/app/plugins/modules/backend-configuration-pn/models';

@Component({
  selector: 'app-documents-folder-create',
  templateUrl: './documents-folder-create.component.html',
  styleUrls: ['./documents-folder-create.component.scss']
})
export class DocumentsFolderCreateComponent implements OnInit {
  @ViewChild('frame') frame;
  @Output() folderCreate: EventEmitter<DocumentFolderModel> = new EventEmitter<
    DocumentFolderModel
  >();
  @Output() folderCreated: EventEmitter<void> = new EventEmitter<void>();
  @Output() folderCreateCancelled: EventEmitter<void> = new EventEmitter<void>();
  name = '';
  selectedParentFolder: FolderDto;
  newFolderModel: FolderCreateModel = new FolderCreateModel();
  // folderTranslations: FormArray = new FormArray([]);
  selectedLanguage: number;

  get languages() {
    return applicationLanguagesTranslated;
  }

  constructor(
    public backendConfigurationPnDocumentsService: BackendConfigurationPnDocumentsService,
    private toastrService: ToastrService,
    private translateService: TranslateService,
    localeService: LocaleService) {
    this.selectedLanguage = applicationLanguagesTranslated.find(
      (x) => x.locale === localeService.getCurrentUserLocale()
    ).id;
  }

  ngOnInit() {}

  show() {
    this.initCreateForm();
    this.frame.show();
  }

  initCreateForm() {
    this.newFolderModel = new FolderCreateModel();
    for (const language of applicationLanguagesTranslated) {
      this.newFolderModel = {
        ...this.newFolderModel,
        translations: [
          ...this.newFolderModel.translations,
          { languageId: language.id, description: '', name: '' },
        ],
      };
    }
  }

  hide() {
    this.frame.hide();
    this.name = '';
  }

  createItem() {
    // this.tagCreate.emit({ name: this.name } as SharedTagCreateModel);
    this.name = '';
  }

  cancelCreate() {
    this.frame.hide();
    this.folderCreateCancelled.emit();
    this.name = '';
  }

  createFolder() {
    // Validate if at least one translation is filled correctly
    const translationExists = this.newFolderModel.translations.find(
      (x) => x.name
    );
    if (translationExists) {
      this.backendConfigurationPnDocumentsService
        .createFolder({
          documentFolderTranslations: this.newFolderModel.translations
        })
        .subscribe((data) => {
          if (data && data.success) {
            this.selectedParentFolder = null;
            this.initCreateForm();
            this.folderCreated.emit();
            this.frame.hide();
            this.folderCreate.emit(data.model);
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
}
