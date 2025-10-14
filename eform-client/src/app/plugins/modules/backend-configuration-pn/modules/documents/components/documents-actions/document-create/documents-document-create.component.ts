import {Component, EventEmitter, OnInit,} from '@angular/core';
import {FormBuilder, FormGroup, FormArray, FormControl} from '@angular/forms';
import {applicationLanguages2, PARSING_DATE_FORMAT} from 'src/app/common/const';
import {CommonDictionaryModel,} from 'src/app/common/models';
import {
  DocumentModel,
  DocumentSimpleFolderModel,
  DocumentPropertyModel,
} from '../../../../../models';
import {
  BackendConfigurationPnDocumentsService,
  BackendConfigurationPnPropertiesService
} from '../../../../../services';
import {Subscription} from 'rxjs';
import {format, set} from 'date-fns';
import * as R from 'ramda';
import {LocaleService, TemplateFilesService} from 'src/app/common/services';
import {MatDialogRef} from '@angular/material/dialog';
import {saveAs} from 'file-saver';
import {MatDatepickerInputEvent} from '@angular/material/datepicker';
import {selectCurrentUserLanguageId} from 'src/app/state/auth/auth.selector';
import {Store} from '@ngrx/store';

@Component({
  selector: 'app-documents-document-create',
  templateUrl: './documents-document-create.component.html',
  styleUrls: ['./documents-document-create.component.scss'],
  standalone: false
})
export class DocumentsDocumentCreateComponent implements OnInit {
  form: FormGroup;
  newDocumentModel: DocumentModel = new DocumentModel();
  // selectedFolder: number;
  documentCreated: EventEmitter<void> = new EventEmitter<void>();
  folders: DocumentSimpleFolderModel[] = [];
  availableProperties: CommonDictionaryModel[] = [];
  selectedLanguage: number;
  documentProperties: number[] = [];

  getSimpleFoldersSub$: Subscription;
  getPropertiesDictionary$: Subscription;
  pdfSub$: Subscription;
  private selectCurrentUserLanguageId$ = this.authStore.select(selectCurrentUserLanguageId);

  get languages() {
    return applicationLanguages2;
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
    const index = this.newDocumentModel.documentTranslations.findIndex(
      x => x.languageId === languageId && x.extensionFile === extension
    );
    if (index !== -1) {
      return this.newDocumentModel.documentTranslations[index];
    }
  }

  constructor(
    private fb: FormBuilder,
    private authStore: Store,
    private templateFilesService: TemplateFilesService,
    private propertiesService: BackendConfigurationPnPropertiesService,
    private backendConfigurationPnDocumentsService: BackendConfigurationPnDocumentsService,
    localeService: LocaleService,
    public dialogRef: MatDialogRef<DocumentsDocumentCreateComponent>,
  ) {
    this.selectCurrentUserLanguageId$.subscribe((languageId) => {
      this.selectedLanguage = languageId;
    });
    // this.selectedLanguage = applicationLanguages2.find(
    //   (x) => x.locale === localeService.getCurrentUserLocale()
    // ).id;
  }


  ngOnInit(): void {
    this.getFolders();
    this.initCreateForm();
    this.getPropertiesDictionary();
  }

  initCreateForm() {
    this.newDocumentModel = new DocumentModel();

    this.form = this.fb.group({
      status: new FormControl(this.newDocumentModel.status),
      folderId: new FormControl(null),
      documentProperties: new FormControl([]),
      translations: this.fb.array([]),
    });

    const translationsArray = this.form.get('translations') as FormArray;

    for (const language of applicationLanguages2) {
      const extension = 'pdf';

      this.newDocumentModel.documentUploadedDatas.push({
        languageId: language.id,
        name: '',
        file: null,
        extension
      });
      this.newDocumentModel.documentTranslations.push({
        languageId: language.id,
        description: '',
        name: '',
        extensionFile: extension
      });

      translationsArray.push(
        this.fb.group({
          languageId: [language.id],
          extension: [extension],
          name: [''],
          description: [''],
        })
      );
    }
  }

  get translationsArray() {
    return this.form.get('translations') as FormArray;
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
    this.newDocumentModel.endDate = date;
  }

  hide() {
    this.dialogRef.close();
  }

  createDocument() {
    const formValue = this.form.value;

    this.newDocumentModel.status = formValue.status;
    this.newDocumentModel.folderId = formValue.folderId;

    const selectedProps: number[] = Array.isArray(formValue.documentProperties)
      ? formValue.documentProperties
      : [];

    this.newDocumentModel.documentProperties = selectedProps.map((id: number) => ({
      propertyId: id,
      documentId: this.newDocumentModel.id
    }));

    const formTranslations = Array.isArray(formValue.translations) ? formValue.translations : [];
    this.newDocumentModel.documentTranslations = this.newDocumentModel.documentTranslations.map(t => {
      const control = formTranslations.find(
        (c: any) => c.languageId === t.languageId && c.extension === t.extensionFile
      );
      if (control) {
        t.name = control.name;
        t.description = control.description;
      }
      return t;
    });

    if (this.newDocumentModel.endDate) {
      this.newDocumentModel.endDate = format(this.newDocumentModel.endDate as Date, PARSING_DATE_FORMAT);
    }

    this.backendConfigurationPnDocumentsService.createDocument(this.newDocumentModel)
      .subscribe((data) => {
        if (data && data.success) {
          this.documentCreated.emit();
          this.hide();
        }
      });
  }


  getFolders() {
    this.getSimpleFoldersSub$ = this.backendConfigurationPnDocumentsService.getSimpleFolders(this.selectedLanguage)
      .subscribe((data) => {
        if (data && data.success && data.model) {
          this.folders = data.model;
        }
      });
  }

  getPropertiesDictionary() {
    this.getPropertiesDictionary$ = this.propertiesService
      .getAllPropertiesDictionary()
      .subscribe((operation) => {
        if (operation && operation.success && operation.model) {
          this.availableProperties = operation.model;
        }
      });
  }

  addToArray(documentProperties: number[]) {
    const originalArray = this.newDocumentModel.documentProperties;
    this.newDocumentModel.documentProperties = [...documentProperties].map(propertyId => {
      const assignmentObject = originalArray.find(x => x.propertyId === propertyId);
      if (assignmentObject) {
        return assignmentObject;
      }
      return {propertyId: propertyId, documentId: this.newDocumentModel.id}
    });
    this.documentProperties = this.newDocumentModel.documentProperties.map(x => x.propertyId);
  }

  getAssignmentIsCheckedByPropertyId(propertyId: number): boolean {
    const assignment = this.newDocumentModel.documentProperties.find(
      (x) => x.propertyId === propertyId
    );
    return assignment !== undefined;
  }

  getAssignmentByPropertyId(propertyId: number): DocumentPropertyModel {
    return (
      this.newDocumentModel.documentProperties.find((x) => x.propertyId === propertyId) ?? {
        propertyId: propertyId,
        documentId: this.newDocumentModel.id,
      }
    );
  }

  removeFile(selectedLanguage: number, extension: string) {
    const filesIndexByLanguage = this.newDocumentModel.documentUploadedDatas.findIndex(
      (x) => (x.languageId === selectedLanguage || x.id === selectedLanguage)
        && x.extension === extension
    );

    if (filesIndexByLanguage !== -1) {
      this.newDocumentModel.documentUploadedDatas[filesIndexByLanguage].file = null;
      this.newDocumentModel.documentUploadedDatas[filesIndexByLanguage].name = '';
    }
  }

  onFileSelected(event: Event, selectedLanguage: number, extension: string) {
    // @ts-ignore
    const files: File[] = event.target.files;
    const file: File = R.last(files);
    if (file.name.toLowerCase().indexOf(extension) === -1) {
      return;
    }
    const filesIndexByLanguage = this.newDocumentModel.documentUploadedDatas.findIndex(
      (x) => (x.languageId === selectedLanguage || x.id === selectedLanguage)
        && x.extension === extension
    );
    if (filesIndexByLanguage !== -1) {
      this.newDocumentModel.documentUploadedDatas[filesIndexByLanguage].file = file;
      const filename = file ? file.name : '';
      this.newDocumentModel.documentUploadedDatas[filesIndexByLanguage].name = file ? file.name : '';
      const fileTranslationsIndexByLanguage = this.newDocumentModel.documentTranslations.findIndex(
        (x) => (x.languageId === selectedLanguage/* || x.id === selectedLanguage*/)
          && x.extensionFile === extension
      );
      if (fileTranslationsIndexByLanguage !== -1) {
        const translation = this.newDocumentModel.documentTranslations[fileTranslationsIndexByLanguage].name;
        this.newDocumentModel.documentTranslations[fileTranslationsIndexByLanguage].name = translation ?
          translation :
          filename.replace(/\.(pdf|doc|docx|dot)$/, ''); // Remove the extension if it is pdf, doc, docx, or dot.
      }
    }
  }

  getFileNameByLanguage(languageId: number, extension: string = 'pdf'): string {
    const index = this.newDocumentModel.documentUploadedDatas
      .findIndex((x) => x.languageId === languageId && x.extension === extension);
    if (index !== -1) {
      const documentUploadedData = this.newDocumentModel.documentUploadedDatas[index];
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
    const index = this.newDocumentModel.documentUploadedDatas
      .findIndex((x) => x.languageId === languageId && x.extension === extension);
    if (index !== -1) {
      const documentUploadedData = this.newDocumentModel.documentUploadedDatas[index];
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
    const documentTranslationFrom = this.newDocumentModel.documentTranslations.filter(x => x.languageId === fromLanguageId);
    const documentUploadedDataFrom = this.newDocumentModel.documentUploadedDatas.filter(x => x.languageId === fromLanguageId);
    const documentTranslationTo = this.newDocumentModel.documentTranslations.filter(x => x.languageId === toLanguageId);
    const documentUploadedDataTo = this.newDocumentModel.documentUploadedDatas.filter(x => x.languageId === toLanguageId);
    this.newDocumentModel.documentTranslations = [
      ...this.newDocumentModel.documentTranslations.filter(x => x.languageId !== toLanguageId),
      ...documentTranslationTo.map(x => {
          const documentTranslationModel = documentTranslationFrom.find(y => y.extensionFile === x.extensionFile);
          return {...x, name: documentTranslationModel.name, description: documentTranslationModel.description};
        }
      )
    ];
    this.newDocumentModel.documentUploadedDatas = [
      ...this.newDocumentModel.documentUploadedDatas.filter(x => x.languageId !== toLanguageId),
      ...documentUploadedDataTo.map(x => {
          const documentUploadedDataModel = documentUploadedDataFrom.find(y => y.extension === x.extension);
          return {
            ...x,
            name: documentUploadedDataModel.name,
            file: documentUploadedDataModel.file,
            fileName: documentUploadedDataModel.fileName,
          };
        }
      )
    ];
  }
}
