import {
  Component,
  EventEmitter,
  OnInit,
  inject
} from '@angular/core';
import {FolderCreateModel, FolderDto,} from 'src/app/common/models';
import {applicationLanguages2} from 'src/app/common/const';
import {LocaleService} from 'src/app/common/services';
import {ToastrService} from 'ngx-toastr';
import {TranslateService} from '@ngx-translate/core';
import {BackendConfigurationPnDocumentsService} from '../../../../../services';
import {DocumentFolderModel} from '../../../../../models';
import {MatDialogRef} from '@angular/material/dialog';
import {selectCurrentUserLanguageId} from 'src/app/state/auth/auth.selector';
import {Store} from '@ngrx/store';

@Component({
    selector: 'app-documents-folder-create',
    templateUrl: './documents-folder-create.component.html',
    styleUrls: ['./documents-folder-create.component.scss'],
    standalone: false
})
export class DocumentsFolderCreateComponent implements OnInit {
  private authStore = inject(Store);
  public backendConfigurationPnDocumentsService = inject(BackendConfigurationPnDocumentsService);
  private toastrService = inject(ToastrService);
  private translateService = inject(TranslateService);
  private localeService = inject(LocaleService);
  public dialogRef = inject(MatDialogRef<DocumentsFolderCreateComponent>);

  folderCreate: EventEmitter<void> = new EventEmitter<void>();
  name = '';
  selectedParentFolder: FolderDto;
  newFolderModel: FolderCreateModel = new FolderCreateModel();
  // folderTranslations: FormArray = new FormArray([]);
  selectedLanguage: number;
  private selectCurrentUserLanguageId$ = this.authStore.select(selectCurrentUserLanguageId);

  get languages() {
    return applicationLanguages2;
  }

  

  ngOnInit() {
    this.selectCurrentUserLanguageId$.subscribe((languageId) => {
      this.selectedLanguage = languageId;
    });
    // this.selectedLanguage = applicationLanguages2.find(
    //   (x) => x.locale === localeService.getCurrentUserLocale()
    // ).id;

    this.initCreateForm();
  }

  initCreateForm() {
    this.newFolderModel = new FolderCreateModel();
    for (const language of applicationLanguages2) {
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
    this.dialogRef.close();
    this.name = '';
  }

  createFolder() {
    // Validate if at least one translation is filled correctly
    if (this.newFolderModel.translations.some(x => x.name)) {
      this.backendConfigurationPnDocumentsService
        .createFolder({
          documentFolderTranslations: this.newFolderModel.translations,
          isDeletable: true,
        })
        .subscribe((data) => {
          if (data && data.success) {
            this.selectedParentFolder = null;
            this.initCreateForm();
            this.folderCreate.emit();
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
