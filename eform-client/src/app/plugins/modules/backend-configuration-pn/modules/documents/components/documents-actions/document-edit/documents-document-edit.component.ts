import {Component, EventEmitter, OnInit, Output, ViewChild} from '@angular/core';
import {DocumentFolderModel, DocumentFolderRequestModel, DocumentModel} from 'src/app/plugins/modules/backend-configuration-pn/models';
import {CommonDictionaryModel, Paged} from 'src/app/common/models';
import {Subscription} from 'rxjs';
import {applicationLanguagesTranslated} from 'src/app/common/const';
import {
  BackendConfigurationPnDocumentsService,
  BackendConfigurationPnPropertiesService
} from 'src/app/plugins/modules/backend-configuration-pn/services';
import {format, set} from 'date-fns';
import {DocumentPropertyModel} from 'src/app/plugins/modules/backend-configuration-pn/models/documents/document-property.model';
import * as R from 'ramda';
import {TemplateFilesService} from 'src/app/common/services';

@Component({
  selector: 'app-documents-document-edit',
  templateUrl: './documents-document-edit.component.html',
  styleUrls: ['./documents-document-edit.component.scss']
})
export class DocumentsDocumentEditComponent implements OnInit {
  @ViewChild('frame') frame;
  newDocumentModel: DocumentModel = new DocumentModel();
  selectedFolder: number;
  @Output() documentCreated: EventEmitter<void> = new EventEmitter<void>();
  folders: Paged<DocumentFolderModel>;
  getPropertiesDictionary$: Subscription;
  availableProperties: CommonDictionaryModel[];
  pdfSub$: Subscription;

  get languages() {
    return applicationLanguagesTranslated;
  }
  constructor(
    private templateFilesService: TemplateFilesService,
    private propertiesService: BackendConfigurationPnPropertiesService,
    private backendConfigurationPnDocumentsService: BackendConfigurationPnDocumentsService) {}

  ngOnInit(): void {}

  show(documentModel: DocumentModel) {
    this.getFolders();
    this.frame.show();
    this.newDocumentModel = documentModel;
    this.selectedFolder = documentModel.folderId;
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


  updateDocument() {
    this.newDocumentModel.folderId = this.selectedFolder;
    this.backendConfigurationPnDocumentsService.updateDocument(this.newDocumentModel)
      .subscribe((data) => {
        // debugger;
        if (data && data.success) {
          this.documentCreated.emit();
          this.hide();
        }
      });
    // this.folderCreate.emit(this.newFolderModel);
    // this.folderCreated.emit();
    // this.name = '';
  }

  getFolders() {
    const requestModel = new DocumentFolderRequestModel();

    this.backendConfigurationPnDocumentsService.getAllFolders(requestModel).subscribe((data) => {
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
          // this.initCreateForm();
        }
      });
  }

  addToArray(e: any, propertyId: number) {
    const assignmentObject = new DocumentPropertyModel();
    if (e.target.checked) {
      // assignmentObject.isChecked = true;
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
    // debugger;
    return assignment === undefined ? false : true;
    // return assignment ? assignment.isChecked : false;
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
    // console.log("in here");
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
