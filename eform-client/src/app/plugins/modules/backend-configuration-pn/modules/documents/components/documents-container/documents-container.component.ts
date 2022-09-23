import {Component, OnDestroy, OnInit, ViewChild} from '@angular/core';
import {
  DocumentsDocumentCreateComponent, DocumentsDocumentEditComponent,
  DocumentsFoldersComponent
} from 'src/app/plugins/modules/backend-configuration-pn/modules/documents/components';
import {BackendConfigurationPnDocumentsService} from 'src/app/plugins/modules/backend-configuration-pn/services';
import {
  DocumentFolderModel,
  DocumentFolderRequestModel,
  DocumentModel,
  DocumentsRequestModel, PropertyModel
} from 'src/app/plugins/modules/backend-configuration-pn/models';
import {Paged} from 'src/app/common/models';
import {LocaleService} from 'src/app/common/services';

@Component({
  selector: 'app-documents-container',
  templateUrl: './documents-container.component.html',
  styleUrls: ['./documents-container.component.scss'],
})
export class DocumentsContainerComponent implements OnInit, OnDestroy {
  @ViewChild('manageFoldersModal') manageFoldersModal: DocumentsFoldersComponent;
  @ViewChild('createDocumentModal') createDocumentModal: DocumentsDocumentCreateComponent;
  @ViewChild('editDocumentModal') editDocumentModal: DocumentsDocumentEditComponent;
  // @ViewChild('editDocumentModal') deleteDocumentModal: DocumentsDocumentEditComponent;
  folders: Paged<DocumentFolderModel>;
  documents: Paged<DocumentModel>;

  constructor(public backendConfigurationPnDocumentsService: BackendConfigurationPnDocumentsService,
              public localeService: LocaleService) {}

  ngOnInit(): void {
    this.getFolders();
  }

  ngOnDestroy(): void {
  }

  openCreateModal() {
    this.createDocumentModal.show();
  }
  openManageFoldersModal() {
    this.manageFoldersModal.show();
  }

  showEditDocumentModal(documentModel: DocumentModel) {
    this.editDocumentModal.show(documentModel);
  }

  // showDeleteDocumentModal(documentModel: DocumentModel) {
  //   this.deleteDocumentModal.show(documentModel);
  // }


  updateTable() {
    // this.getAllWorkOrderCasesSub$ = this.taskManagementStateService
    //   .getAllWorkOrderCases()
    //   .subscribe((data) => {
    //     if (data && data.success && data.model) {
    //       this.workOrderCases = data.model;
    //     }
    //   });
  }

  getFolders() {
    const requestModel = new DocumentFolderRequestModel();

    this.backendConfigurationPnDocumentsService.getAllFolders(requestModel).subscribe((data) => {
      if (data && data.success) {
        this.folders = data.model;
        this.getDocuments();
      }
    });
  }

  getDocuments() {
    const requestModel = new DocumentsRequestModel();
    this.backendConfigurationPnDocumentsService.getAllDocuments(requestModel).subscribe((data) => {
      if (data && data.success) {
        this.documents = data.model;
      }
    });
  }
}
