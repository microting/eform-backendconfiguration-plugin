import {Component, OnDestroy, OnInit} from '@angular/core';
import {
  DocumentsDocumentCreateComponent,
  DocumentsDocumentDeleteComponent,
  DocumentsDocumentEditComponent,
  DocumentsFoldersComponent
} from '../';
import {
  DocumentFolderModel,
  DocumentModel,
} from '../../../../models';
import {Paged} from 'src/app/common/models';
import {LocaleService} from 'src/app/common/services';
import {Subscription} from 'rxjs';
import {DocumentsStateService} from '../../../documents/store';
import {MatDialog} from '@angular/material/dialog';
import {Overlay} from '@angular/cdk/overlay';
import {dialogConfigHelper} from 'src/app/common/helpers';

@Component({
  selector: 'app-documents-container',
  templateUrl: './documents-container.component.html',
  styleUrls: ['./documents-container.component.scss'],
})
export class DocumentsContainerComponent implements OnInit, OnDestroy {
  folders: Paged<DocumentFolderModel> = new Paged<DocumentFolderModel>();
  documents: Paged<DocumentModel> = new Paged<DocumentModel>();
  getFoldersSub$: Subscription;
  documentDeletedSub$: Subscription;
  documentUpdatedSub$: Subscription;
  documentCreatedSub$: Subscription;
  folderManageModalClosedSub$: Subscription;
  getDocumentsSub$: Subscription;

  constructor(
    public dialog: MatDialog,
    private overlay: Overlay,
    public documentsStateService: DocumentsStateService,
    public localeService: LocaleService) {
  }

  ngOnInit(): void {
    this.getFolders();
  }

  ngOnDestroy(): void {
  }

  openCreateModal() {
    const createDocumentModal = this.dialog.open(DocumentsDocumentCreateComponent, {...dialogConfigHelper(this.overlay), minWidth: 500});
    this.documentCreatedSub$ = createDocumentModal.componentInstance.documentCreated.subscribe(() => {
      this.updateTable();
    });
  }

  openManageFoldersModal() {
    const manageFoldersModal = this.dialog.open(DocumentsFoldersComponent, {...dialogConfigHelper(this.overlay)});
    this.folderManageModalClosedSub$ = manageFoldersModal.componentInstance.foldersChanged.subscribe(() => {
      this.getFolders();
    });
  }

  showEditDocumentModal(documentModel: DocumentModel) {
    const editDocumentModal = this.dialog.open(DocumentsDocumentEditComponent, {...dialogConfigHelper(this.overlay, documentModel)});
    this.documentUpdatedSub$ = editDocumentModal.componentInstance.documentUpdated.subscribe(() => {
      this.updateTable();
    });
  }

  showDeleteDocumentModal(documentModel: DocumentModel) {
    const deleteDocument = this.dialog.open(DocumentsDocumentDeleteComponent, {...dialogConfigHelper(this.overlay, documentModel)});
    this.documentDeletedSub$ = deleteDocument.componentInstance.documentDeleted.subscribe(() => {
      this.updateTable();
    });
  }

  getFolders() {
    this.getFoldersSub$ = this.documentsStateService
      .getFolders()
      .subscribe((data) => {
        if (data && data.success && data.model) {
          this.folders = data.model;
        }
      });
  }

  updateTable() {
    this.getDocumentsSub$ = this.documentsStateService.getDocuments().subscribe((data) => {
      if (data && data.success && data.model) {
        this.documents = data.model;
      }
    });
  }

  getDocumentsByFolderId(folderId: number) {
    return this.documents.entities.filter(x => x.folderId === folderId);
  }

  getFolderTranslation(folder: DocumentFolderModel) {
    if(folder.documentFolderTranslations[0].name) {
      return folder.documentFolderTranslations[0].name;
    } else if(folder.documentFolderTranslations.some(x => x.name)) {
      return folder.documentFolderTranslations.filter(x => x.name)[0].name
    }
    return '';
  }
}
