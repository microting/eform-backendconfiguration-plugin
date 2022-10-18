import {Component, EventEmitter, OnInit, Output, ViewChild} from '@angular/core';
import {
  DocumentFolderModel,
  DocumentFolderRequestModel,
  DocumentModel,
  DocumentSimpleFolderModel
} from 'src/app/plugins/modules/backend-configuration-pn/models';
import {CommonDictionaryModel, Paged} from 'src/app/common/models';
import {Subscription} from 'rxjs';
import {applicationLanguages2, applicationLanguagesTranslated} from 'src/app/common/const';
import {
  BackendConfigurationPnDocumentsService,
  BackendConfigurationPnPropertiesService
} from 'src/app/plugins/modules/backend-configuration-pn/services';
import {format, set} from 'date-fns';
import {DocumentPropertyModel} from 'src/app/plugins/modules/backend-configuration-pn/models/documents/document-property.model';
import * as R from 'ramda';
import {LocaleService, TemplateFilesService} from 'src/app/common/services';

@Component({
  selector: 'app-documents-document-edit',
  templateUrl: './documents-document-edit.component.html',
  styleUrls: ['./documents-document-edit.component.scss']
})
export class DocumentsDocumentEditComponent implements OnInit {
  @ViewChild('frame') frame;
  newDocumentModel: DocumentModel = new DocumentModel();
  selectedFolder: number;
  @Output() documentUpdated: EventEmitter<void> = new EventEmitter<void>();
  folders: DocumentSimpleFolderModel[];
  getPropertiesDictionary$: Subscription;
  availableProperties: CommonDictionaryModel[];
  pdfSub$: Subscription;
  documentSub$: Subscription;
  selectedLanguage: number;

  get languages() {
    return applicationLanguages2;
  }
  constructor(
    private templateFilesService: TemplateFilesService,
    private propertiesService: BackendConfigurationPnPropertiesService,
    private backendConfigurationPnDocumentsService: BackendConfigurationPnDocumentsService,
    localeService: LocaleService) {
    this.selectedLanguage = applicationLanguages2.find(
      (x) => x.locale === localeService.getCurrentUserLocale()
    ).id;
  }

  ngOnInit(): void {}

  show(documentModel: DocumentModel) {
    // this.frame.show();
    this.getDocument(documentModel.id);
    // this.selectedFolder = documentModel.folderId;
  }

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
    this.frame.hide();
  }
  cancelCreate() {
    this.frame.hide();
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
    const requestModel = new DocumentFolderRequestModel();

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
          this.frame.show();
        }
      });
  }

  addToArray(e: any, propertyId: number) {
    const assignmentObject = new DocumentPropertyModel();
    if (e.target.checked) {
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
    return assignment === undefined ? false : true;
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
      this.newDocumentModel.documentUploadedDatas[filesIndexByLanguage].file = R.last(files);
      this.newDocumentModel.documentUploadedDatas[filesIndexByLanguage].name = R.last(files).name;
    }
  }

  getFileNameByLanguage(languageId: number): string {
    if (this.newDocumentModel.documentUploadedDatas.length>0) {
      if (this.newDocumentModel.documentUploadedDatas.find((x) => x.languageId == languageId).id) {
        return this.newDocumentModel.documentUploadedDatas.find((x) => x.languageId == languageId).name;
      } else {
        // return '';
        const file = this.newDocumentModel.documentUploadedDatas.find((x) => x.languageId == languageId).file;
        if (file) {
          return file.name;
        }
      }
    }
  }

  getPdf(languageId: number) {
    // TODO: CHECK
    const fileName = this.newDocumentModel.documentUploadedDatas.find((x) => x.languageId == languageId).fileName;
    this.pdfSub$ = this.templateFilesService.getImage(fileName).subscribe((blob) => {
      const fileURL = URL.createObjectURL(blob);
      window.open(fileURL, '_blank');
    });
  }
}
