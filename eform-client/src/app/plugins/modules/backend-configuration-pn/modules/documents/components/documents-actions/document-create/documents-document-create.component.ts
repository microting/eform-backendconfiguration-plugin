import {Component, EventEmitter, OnInit, Output, ViewChild} from '@angular/core';
import {applicationLanguagesTranslated} from 'src/app/common/const';
import {CommonDictionaryModel, FolderCreateModel, Paged} from 'src/app/common/models';
import {
  DocumentFolderModel,
  DocumentFolderRequestModel,
  DocumentModel,
  PropertyAssignmentWorkerModel
} from 'src/app/plugins/modules/backend-configuration-pn/models';
import {
  BackendConfigurationPnDocumentsService,
  BackendConfigurationPnPropertiesService
} from 'src/app/plugins/modules/backend-configuration-pn/services';
import {Subscription} from 'rxjs';
import {format, set} from 'date-fns';
import {DocumentPropertyModel} from 'src/app/plugins/modules/backend-configuration-pn/models/documents/document-property.model';

@Component({
  selector: 'app-documents-document-create',
  templateUrl: './documents-document-create.component.html',
  styleUrls: ['./documents-document-create.component.scss']
})
export class DocumentsDocumentCreateComponent implements OnInit {
  @ViewChild('frame') frame;
  newDocumentModel: DocumentModel = new DocumentModel();
  selectedFolder: number;
  @Output() documentCreated: EventEmitter<void> = new EventEmitter<void>();
  folders: Paged<DocumentFolderModel>;
  getPropertiesDictionary$: Subscription;
  availableProperties: CommonDictionaryModel[];
  // assignments: DocumentPropertyModel[] = [];

  get languages() {
    return applicationLanguagesTranslated;
  }
  constructor(
    private propertiesService: BackendConfigurationPnPropertiesService,
    private backendConfigurationPnDocumentsService: BackendConfigurationPnDocumentsService) {}

  ngOnInit(): void {}

  show() {
    this.getFolders();
    this.initCreateForm();
    this.frame.show();
    this.getPropertiesDictionary();
  }

  initCreateForm() {
    this.newDocumentModel = new DocumentModel();
    for (const language of applicationLanguagesTranslated) {
      this.newDocumentModel = {
        ...this.newDocumentModel,
        documentTranslations: [
          ...this.newDocumentModel.documentTranslations,
          { languageId: language.id, description: '', name: '' },
        ],
      };
    }
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

  createDocument() {
    debugger;
    this.newDocumentModel.folderId = this.selectedFolder;
    this.backendConfigurationPnDocumentsService.createDocument(this.newDocumentModel)
      .subscribe((data) => {
      debugger;
      if (data && data.success) {
        this.documentCreated.emit();
        this.hide();
      }
    });
  }

  getFolders() {
    const requestModel = new DocumentFolderRequestModel();

    this.backendConfigurationPnDocumentsService.getAllFolders(requestModel).subscribe((data) => {
      if (data && data.success) {
        this.folders = data.model;
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
}
