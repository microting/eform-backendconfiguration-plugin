import {Component, EventEmitter, Inject, OnInit} from '@angular/core';
import {
  DocumentFolderRequestModel,
  DocumentModel,
  DocumentPropertyModel,
  DocumentSimpleFolderModel
} from '../../../../../models';
import {CommonDictionaryModel,} from 'src/app/common/models';
import {Subscription} from 'rxjs';
import {applicationLanguages2, PdfIcon} from 'src/app/common/const';
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

  onFileSelected(event: Event, selectedLanguage: number) {
    // @ts-ignore
    const files: File[] = event.target.files;
    const filesIndexByLanguage = this.newDocumentModel.documentUploadedDatas.findIndex(
      (x) => x.languageId === selectedLanguage || x.id === selectedLanguage
    );
    if (filesIndexByLanguage !== -1) {
      const file: File = R.last(files);
      this.newDocumentModel.documentUploadedDatas[filesIndexByLanguage].file = file;
      this.newDocumentModel.documentUploadedDatas[filesIndexByLanguage].name = file.name;
    }
  }

  getFileNameByLanguage(languageId: number): string {
    const index = this.newDocumentModel.documentUploadedDatas.findIndex((x) => x.languageId === languageId);
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

  getPdf(languageId: number) {
    const index = this.newDocumentModel.documentUploadedDatas.findIndex((x) => x.languageId === languageId);
    if (index !== -1) {
      const documentUploadedData = this.newDocumentModel.documentUploadedDatas[index];
      if (documentUploadedData.id) {
        this.pdfSub$ = this.templateFilesService.getImage(documentUploadedData.fileName).subscribe((blob) => {
          const fileURL = URL.createObjectURL(blob);
          window.open(fileURL, '_blank');
        });
      } else {
        window.open(URL.createObjectURL(documentUploadedData.file), '_blank');
      }
    }
  }
}
