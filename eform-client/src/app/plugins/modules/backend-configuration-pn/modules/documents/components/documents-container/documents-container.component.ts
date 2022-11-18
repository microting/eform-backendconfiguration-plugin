import {Component, OnDestroy, OnInit, ViewChild} from '@angular/core';
import {
  DocumentsDocumentCreateComponent, DocumentsDocumentDeleteComponent, DocumentsDocumentEditComponent,
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
import {Subscription} from 'rxjs';
import {DocumentsStateService} from 'src/app/plugins/modules/backend-configuration-pn/modules/documents/store';
import {MatDialog} from '@angular/material/dialog';
import {Overlay} from '@angular/cdk/overlay';
import {dialogConfigHelper} from 'src/app/common/helpers';

@Component({
  selector: 'app-documents-container',
  templateUrl: './documents-container.component.html',
  styleUrls: ['./documents-container.component.scss'],
})
export class DocumentsContainerComponent implements OnInit, OnDestroy {
  @ViewChild('manageFoldersModal') manageFoldersModal: DocumentsFoldersComponent;
  @ViewChild('createDocumentModal') createDocumentModal: DocumentsDocumentCreateComponent;
  @ViewChild('editDocumentModal') editDocumentModal: DocumentsDocumentEditComponent;
  @ViewChild('deleteDocumentModal') deleteDocumentModal: DocumentsDocumentEditComponent;
  folders: Paged<DocumentFolderModel>;
  documents: Paged<DocumentModel>;
  subscription: Subscription;
  documentDeletedSub$: Subscription;
  documentUpdatedSub$: Subscription;
  documentCreatedSub$: Subscription;
  folderManageModalClosedSub$: Subscription;

  constructor(
    public dialog: MatDialog,
    private overlay: Overlay,
    public documentsStateService: DocumentsStateService,
    public localeService: LocaleService) {}

  ngOnInit(): void {
    //this.getFolders();
  }

  ngOnDestroy(): void {
  }

  openCreateModal() {
    this.dialog.open(DocumentsDocumentCreateComponent, {...dialogConfigHelper(this.overlay)});
    this.documentCreatedSub$ = this.createDocumentModal.documentCreated.subscribe(() => {
      this.updateTable();
    });
    //this.createDocumentModal.show();
  }
  openManageFoldersModal() {
    this.dialog.open(DocumentsFoldersComponent, {...dialogConfigHelper(this.overlay)});
    this.folderManageModalClosedSub$ = this.manageFoldersModal.foldersChanged.subscribe(() => {
      this.updateTable();
    });
    //this.manageFoldersModal.show();
  }

  showEditDocumentModal(documentModel: DocumentModel) {
    this.dialog.open(DocumentsDocumentEditComponent, {...dialogConfigHelper(this.overlay, documentModel)});
    this.documentUpdatedSub$ = this.editDocumentModal.documentUpdated.subscribe(() => {
      this.updateTable();
    });
    //this.editDocumentModal.show(documentModel);
  }

  showDeleteDocumentModal(documentModel: DocumentModel) {
    const deleteDocument = this.dialog.open(DocumentsDocumentDeleteComponent, {...dialogConfigHelper(this.overlay, documentModel)});
    this.documentDeletedSub$ = deleteDocument.componentInstance.documentDeleted.subscribe(() => {
      this.updateTable();
    });
    //this.deleteDocumentModal.show(documentModel);
  }


  updateTable() {
     this.subscription = this.documentsStateService
       .getFolders()
       .subscribe((data) => {
         if (data && data.success && data.model) {
           this.folders = data.model;
           this.documentsStateService.getDocuments().subscribe((data) => {
              if (data && data.success && data.model) {
                this.documents = data.model;
              }
           });
    //       this.workOrderCases = data.model;
         }
       });
  }
}
