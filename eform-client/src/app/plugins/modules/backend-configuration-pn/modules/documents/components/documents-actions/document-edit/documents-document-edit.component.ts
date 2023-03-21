import {Component, EventEmitter, Inject, OnInit} from '@angular/core';
import {
  DocumentFolderRequestModel,
  DocumentModel,
  DocumentPropertyModel,
  DocumentSimpleFolderModel
} from '../../../../../models';
import {CommonDictionaryModel,} from 'src/app/common/models';
import {Subscription} from 'rxjs';
import {applicationLanguages2, PdfIcon, WordIcon} from 'src/app/common/const';
import {
  BackendConfigurationPnDocumentsService,
  BackendConfigurationPnPropertiesService
} from '../../../../../services';
import {format, set} from 'date-fns';
import * as R from 'ramda';
import {LocaleService, TemplateFilesService} from 'src/app/common/services';
import {MAT_DIALOG_DATA, MatDialogRef} from '@angular/material/dialog';
import {MatIconRegistry} from '@angular/material/icon';
import {DomSanitizer} from '@angular/platform-browser';

@Component({
  selector: 'app-documents-document-edit',
  templateUrl: './documents-document-edit.component.html',
  styleUrls: ['./documents-document-edit.component.scss']
})
export class DocumentsDocumentEditComponent implements OnInit {
  newDocumentModel: DocumentModel = new DocumentModel();
  selectedFolder: number;
  documentUpdated: EventEmitter<void> = new EventEmitter<void>();
  folders: DocumentSimpleFolderModel[];
  availableProperties: CommonDictionaryModel[];
  selectedLanguage: number;

  pdfSub$: Subscription;
  documentSub$: Subscription;
  getPropertiesDictionary$: Subscription;

  get languages() {
    return applicationLanguages2;
  }

  getLanguageByLanguageId(languageId: number) {
    const languages = this.languages.filter(x => x.id === languageId);
    if(languages && languages.length > 0) {
      return languages[0];
    }
    return this.languages[0];
  }

  getTranslateByLanguageId(languageId: number, extension: string) {
    const index = this.newDocumentModel.documentTranslations.findIndex(
      x => x.languageId === languageId && x.extensionFile === extension
    );
    if(index !== -1) {
      return this.newDocumentModel.documentTranslations[index];
    }
    return {name: '', description: '', extensionFile: extension, languageId: languageId}
  }

  constructor(
    private templateFilesService: TemplateFilesService,
    private propertiesService: BackendConfigurationPnPropertiesService,
    private backendConfigurationPnDocumentsService: BackendConfigurationPnDocumentsService,
    localeService: LocaleService,
    public dialogRef: MatDialogRef<DocumentsDocumentEditComponent>,
    @Inject(MAT_DIALOG_DATA) documentModel: DocumentModel,
    iconRegistry: MatIconRegistry,
    sanitizer: DomSanitizer,
    ) {
    this.getDocument(documentModel.id);
    this.selectedLanguage = applicationLanguages2.find(
      (x) => x.locale === localeService.getCurrentUserLocale()
    ).id;
    iconRegistry.addSvgIconLiteral('file-pdf', sanitizer.bypassSecurityTrustHtml(PdfIcon));
    iconRegistry.addSvgIconLiteral('file-word', sanitizer.bypassSecurityTrustHtml(WordIcon));
  }

  ngOnInit(): void {}

  updateStartDate(e: any) {
    let date = new Date(e);
    date = set(date, {
      hours: 0,
      minutes: 0,
      seconds: 0,
      milliseconds: 0,
    });
    this.newDocumentModel.endDate = format(
      date,
      `yyyy-MM-dd'T'HH:mm:ss.SSS'Z'`
    );
  }

  hide() {
    this.dialogRef.close();
  }

  getDocument(documentId: number) {
    this.documentSub$ = this.backendConfigurationPnDocumentsService.getSingleDocument(documentId).subscribe((data) => {
      if (data && data.success) {
        this.newDocumentModel = data.model;
        this.selectedFolder = this.newDocumentModel.folderId;
        this.getFolders();
      }
    });
  }


  updateDocument() {
    this.newDocumentModel.folderId = this.selectedFolder;
    this.backendConfigurationPnDocumentsService.updateDocument(this.newDocumentModel)
      .subscribe((data) => {
        if (data && data.success) {
          this.documentUpdated.emit();
          this.hide();
        }
      });
  }

  getFolders() {
    this.backendConfigurationPnDocumentsService.getSimpleFolders(this.selectedLanguage).subscribe((data) => {
      if (data && data.success) {
        this.folders = data.model;
        this.getPropertiesDictionary();
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

  addToArray(checked: boolean, propertyId: number) {
    const assignmentObject = new DocumentPropertyModel();
    if (checked) {
      assignmentObject.propertyId = propertyId;
      this.newDocumentModel.documentProperties = [...this.newDocumentModel.documentProperties, assignmentObject];
    } else {
      this.newDocumentModel.documentProperties = this.newDocumentModel.documentProperties.filter(
        (x) => x.propertyId !== propertyId
      );
    }
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


  onFileSelected(event: Event, selectedLanguage: number, extension: string) {
    // @ts-ignore
    const files: File[] = event.target.files;
    const file: File = R.last(files);
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
    } else {
      const filename = file ? file.name : '';
      this.newDocumentModel.documentUploadedDatas = [...this.newDocumentModel.documentUploadedDatas,
        {
          file: file,
          fileName: (file ? file.name : ''),
          name: (file ? file.name : ''),
          extension: extension,
          languageId: selectedLanguage,
        }];
      const fileTranslationsIndexByLanguage = this.newDocumentModel.documentTranslations.findIndex(
        (x) => (x.languageId === selectedLanguage/* || x.id === selectedLanguage*/)
          && x.extensionFile === extension
      );
      if (fileTranslationsIndexByLanguage !== -1) {
        const translation = this.newDocumentModel.documentTranslations[fileTranslationsIndexByLanguage].name;
        this.newDocumentModel.documentTranslations[fileTranslationsIndexByLanguage].name = translation ?
          translation :
          filename.replace(/\.(pdf|doc|docx|dot)$/, ''); // Remove the extension if it is pdf, doc, docx, or dot.
      } else {
        this.newDocumentModel.documentTranslations = [...this.newDocumentModel.documentTranslations,
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
          const fileURL = URL.createObjectURL(blob);
          window.open(fileURL, '_blank');
        });
      } else {
        window.open(URL.createObjectURL(documentUploadedData.file), '_blank');
      }
    }
  }
}
