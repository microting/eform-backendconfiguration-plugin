import {Component, EventEmitter, Inject, OnInit} from '@angular/core';
import {
  DocumentModel,
  DocumentSimpleFolderModel
} from '../../../../../models';
import { FormArray, FormBuilder, FormControl, FormGroup } from '@angular/forms';
import {CommonDictionaryModel,} from 'src/app/common/models';
import {Subscription} from 'rxjs';
import {applicationLanguages2, PARSING_DATE_FORMAT} from 'src/app/common/const';
import {
  BackendConfigurationPnDocumentsService,
  BackendConfigurationPnPropertiesService
} from '../../../../../services';
import {format, set} from 'date-fns';
import * as R from 'ramda';
import {LocaleService, TemplateFilesService} from 'src/app/common/services';
import {MAT_DIALOG_DATA, MatDialogRef} from '@angular/material/dialog';
import {saveAs} from 'file-saver';
import {MatDatepickerInputEvent} from '@angular/material/datepicker';
import {Store} from '@ngrx/store';
import {selectCurrentUserLanguageId} from 'src/app/state/auth/auth.selector';

@Component({
    selector: 'app-documents-document-edit',
    templateUrl: './documents-document-edit.component.html',
    styleUrls: ['./documents-document-edit.component.scss'],
    standalone: false
})
export class DocumentsDocumentEditComponent implements OnInit {
  form: FormGroup;
  documentModel: DocumentModel = new DocumentModel();
  selectedFolder: number;
  documentUpdated: EventEmitter<void> = new EventEmitter<void>();
  folders: DocumentSimpleFolderModel[];
  availableProperties: CommonDictionaryModel[];
  selectedLanguage: number;
  documentProperties: number[] = [];

  pdfSub$: Subscription;
  documentSub$: Subscription;
  getPropertiesDictionary$: Subscription;
  private selectCurrentUserLanguageId$ = this.authStore.select(selectCurrentUserLanguageId);

  get languages() {
    return applicationLanguages2;
  }
  get translationsArray() {
    return this.form.get('translations') as FormArray;
  }

  get disabledSaveBtn() {
    const translations = this.form?.get('translations')?.value || [];
    return !translations.some((t: any) => !!t.name?.trim());
  }

  getLanguageByLanguageId(languageId: number) {
    const languages = this.languages.filter(x => x.id === languageId);
    if (languages && languages.length > 0) {
      return languages[0];
    }
    return this.languages[0];
  }

  getTranslateByLanguageId(languageId: number, extension: string) {
    const index = this.documentModel.documentTranslations.findIndex(
      x => x.languageId === languageId && x.extensionFile === extension
    );
    if (index !== -1) {
      return this.documentModel.documentTranslations[index];
    }
    return {name: '', description: '', extensionFile: extension, languageId: languageId};
  }

  constructor(
    private fb: FormBuilder,
    private authStore: Store,
    private templateFilesService: TemplateFilesService,
    private propertiesService: BackendConfigurationPnPropertiesService,
    private backendConfigurationPnDocumentsService: BackendConfigurationPnDocumentsService,
    localeService: LocaleService,
    public dialogRef: MatDialogRef<DocumentsDocumentEditComponent>,
    @Inject(MAT_DIALOG_DATA) documentModel: DocumentModel,
  ) {
    this.getDocument(documentModel.id);
    this.selectCurrentUserLanguageId$.subscribe((languageId) => {
      this.selectedLanguage = languageId;
    });
    // this.selectedLanguage = applicationLanguages2.find(
    //   (x) => x.locale === localeService.getCurrentUserLocale()
    // ).id;
  }

  ngOnInit(): void {
    this.getFolders();
    this.getPropertiesDictionary();
  }

  initEditForm() {
    this.form = this.fb.group({
      status: new FormControl(this.documentModel.status),
      folderId: new FormControl(this.documentModel.folderId),
      documentProperties: new FormControl(this.documentModel.documentProperties.map(p => p.propertyId)),
      translations: this.fb.array([]),
    });

    const translationsArray = this.form.get('translations') as FormArray;

    for (const language of this.languages) {
      const existing = this.documentModel.documentTranslations.find(
        t => t.languageId === language.id && t.extensionFile === 'pdf'
      );
      translationsArray.push(
        this.fb.group({
          languageId: [language.id],
          extension: ['pdf'],
          name: [existing?.name || ''],
          description: [existing?.description || ''],
        })
      );
    }
  }

  updateEndDate(e: MatDatepickerInputEvent<any, any>) {
    let date = e.value;
    date = set(date, {
      hours: 0,
      minutes: 0,
      seconds: 0,
      milliseconds: 0,
      date: date.getDate(),
      year: date.getFullYear(),
      month: date.getMonth(),
    });
    this.documentModel.endDate = date;
  }

  hide() {
    this.dialogRef.close();
  }

  getDocument(documentId: number) {
    this.documentSub$ = this.backendConfigurationPnDocumentsService.getSingleDocument(documentId)
      .subscribe((data) => {
        if (data && data.success) {
          this.documentModel = data.model;
          this.documentProperties = this.documentModel.documentProperties.map(x => x.propertyId);
          this.selectedFolder = this.documentModel.folderId;

          this.initEditForm();

          this.getFolders();
          this.getPropertiesDictionary();
        }
      });
  }

  updateDocument() {
    const formValue = this.form.value;

    this.documentModel.status = formValue.status;
    this.documentModel.folderId = formValue.folderId;
    this.documentModel.documentProperties = formValue.documentProperties.map((id: number) => ({
      propertyId: id,
      documentId: this.documentModel.id,
    }));

    const formTranslations = formValue.translations || [];
    this.documentModel.documentTranslations = this.documentModel.documentTranslations.map(t => {
      if (t.extensionFile === 'pdf') {
        const control = formTranslations.find(c => c.languageId === t.languageId);
        if (control) {
          t.name = control.name;
          t.description = control.description;
        }
      }
      return t;
    });

    if (this.documentModel.endDate) {
      this.documentModel.endDate = format(this.documentModel.endDate as Date, PARSING_DATE_FORMAT);
    }

    this.backendConfigurationPnDocumentsService.updateDocument(this.documentModel).subscribe(data => {
      if (data && data.success) {
        this.documentUpdated.emit();
        this.hide();
      }
    });
  }


  removeFile(selectedLanguage: number, extension: string) {
    const filesIndexByLanguage = this.documentModel.documentUploadedDatas.findIndex(
      (x) => (x.languageId === selectedLanguage || x.id === selectedLanguage)
        && x.extension === extension
    );

    if (filesIndexByLanguage !== -1) {
      this.documentModel.documentUploadedDatas[filesIndexByLanguage].file = null;
      this.documentModel.documentUploadedDatas[filesIndexByLanguage].name = '';
      this.documentModel.documentUploadedDatas[filesIndexByLanguage].fileName = '';
      //this.documentModel.documentUploadedDatas[filesIndexByLanguage].hash = '';
    }
  }

  getFolders() {
    this.backendConfigurationPnDocumentsService.getSimpleFolders(this.selectedLanguage)
      .subscribe((data) => {
        if (data && data.success) {
          this.folders = data.model;
          if (this.form && this.documentModel.folderId) {
            this.form.get('folderId')?.setValue(this.documentModel.folderId);
          }
        }
      });
  }

  getPropertiesDictionary() {
    this.getPropertiesDictionary$ = this.propertiesService
      .getAllPropertiesDictionary()
      .subscribe((operation) => {
        if (operation && operation.success) {
          this.availableProperties = operation.model;
        }
      });
  }

  addToArray(documentProperties: number[]) {
    const originalArray = this.documentModel.documentProperties;
    this.documentModel.documentProperties = [...documentProperties].map(propertyId => {
      const assignmentObject = originalArray.find(x => x.propertyId === propertyId);
      if (assignmentObject) {
        return assignmentObject;
      }
      return {propertyId: propertyId, documentId: this.documentModel.id};
    });
    this.documentProperties = this.documentModel.documentProperties.map(x => x.propertyId);
  }

  onFileSelected(event: Event, selectedLanguage: number, extension: string) {
    // @ts-ignore
    const files: File[] = event.target.files;
    const file: File = R.last(files);
    if (file.name.indexOf(extension) === -1) {
      return;
    }
    const filesIndexByLanguage = this.documentModel.documentUploadedDatas.findIndex(
      (x) => (x.languageId === selectedLanguage || x.id === selectedLanguage)
        && x.extension === extension
    );
    if (filesIndexByLanguage !== -1) {
      this.documentModel.documentUploadedDatas[filesIndexByLanguage].file = file;
      const filename = file ? file.name : '';
      this.documentModel.documentUploadedDatas[filesIndexByLanguage].name = file ? file.name : '';
      const fileTranslationsIndexByLanguage = this.documentModel.documentTranslations.findIndex(
        (x) => (x.languageId === selectedLanguage/* || x.id === selectedLanguage*/)
          && x.extensionFile === extension
      );
      if (fileTranslationsIndexByLanguage !== -1) {
        const translation = this.documentModel.documentTranslations[fileTranslationsIndexByLanguage].name;
        this.documentModel.documentTranslations[fileTranslationsIndexByLanguage].name = translation ?
          translation :
          filename.replace(/\.(pdf|doc|docx|dot)$/, ''); // Remove the extension if it is pdf, doc, docx, or dot.
      }
    } else {
      const filename = file ? file.name : '';
      this.documentModel.documentUploadedDatas = [...this.documentModel.documentUploadedDatas,
        {
          file: file,
          fileName: (file ? file.name : ''),
          name: (file ? file.name : ''),
          extension: extension,
          languageId: selectedLanguage,
        }];
      const fileTranslationsIndexByLanguage = this.documentModel.documentTranslations.findIndex(
        (x) => (x.languageId === selectedLanguage/* || x.id === selectedLanguage*/)
          && x.extensionFile === extension
      );
      if (fileTranslationsIndexByLanguage !== -1) {
        const translation = this.documentModel.documentTranslations[fileTranslationsIndexByLanguage].name;
        this.documentModel.documentTranslations[fileTranslationsIndexByLanguage].name = translation ?
          translation :
          filename.replace(/\.(pdf|doc|docx|dot)$/, ''); // Remove the extension if it is pdf, doc, docx, or dot.
      } else {
        this.documentModel.documentTranslations = [...this.documentModel.documentTranslations,
          {
            extensionFile: extension,
            languageId: selectedLanguage,
            description: '',
            name: filename.replace(/\.(pdf|doc|docx|dot)$/, ''), // Remove the extension if it is pdf, doc, docx, or dot.
          }];
      }
    }
  }

  getFileNameByLanguage(languageId: number, extension: string = 'pdf'): string {
    const index = this.documentModel.documentUploadedDatas
      .findIndex((x) => x.languageId === languageId && x.extension === extension);
    if (index !== -1) {
      const documentUploadedData = this.documentModel.documentUploadedDatas[index];
      if (documentUploadedData.id) {
        return documentUploadedData.name;
      } else {
        const file = documentUploadedData.file;
        if (file) {
          return file.name;
        }
      }
    }
    return '';
  }

  getFile(languageId: number, extension: string = 'pdf') {
    const index = this.documentModel.documentUploadedDatas
      .findIndex((x) => x.languageId === languageId && x.extension === extension);
    if (index !== -1) {
      const documentUploadedData = this.documentModel.documentUploadedDatas[index];
      if (documentUploadedData.id) {
        this.pdfSub$ = this.templateFilesService.getImage(documentUploadedData.fileName)
          .subscribe((blob) => {
            saveAs(blob, documentUploadedData.name);
          });
      } else {
        saveAs(documentUploadedData.file, documentUploadedData.name);
      }
    }
  }

  copyValues(fromLanguageId: number, toLanguageId: number) {
    const documentTranslationFrom = this.documentModel.documentTranslations.filter(x => x.languageId === fromLanguageId);
    const documentUploadedDataFrom = this.documentModel.documentUploadedDatas.filter(x => x.languageId === fromLanguageId);
    const documentTranslationTo = this.documentModel.documentTranslations.filter(x => x.languageId === toLanguageId);
    const documentUploadedDataTo = this.documentModel.documentUploadedDatas.filter(x => x.languageId === toLanguageId);
    this.documentModel.documentTranslations = [
      ...this.documentModel.documentTranslations.filter(x => x.languageId !== toLanguageId),
      ...documentTranslationTo.map(x => {
          const documentTranslationModel = documentTranslationFrom.find(y => y.extensionFile === x.extensionFile);
          return {...x, name: documentTranslationModel.name, description: documentTranslationModel.description};
        }
      )
    ];
    this.documentModel.documentUploadedDatas = [
      ...this.documentModel.documentUploadedDatas.filter(x => x.languageId !== toLanguageId),
      ...documentUploadedDataTo.map(x => {
          const documentUploadedDataModel = documentUploadedDataFrom.find(y => y.extension === x.extension);
          return {
            ...x,
            name: documentUploadedDataModel.name,
            file: documentUploadedDataModel.file,
            uploadedDataId: documentUploadedDataModel.id,
            fileName: documentUploadedDataModel.fileName,
          };
        }
      )
    ];
  }
}

