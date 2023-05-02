import {Component, OnDestroy, OnInit} from '@angular/core';
import {
  DocumentsDocumentCreateComponent,
  DocumentsDocumentDeleteComponent,
  DocumentsDocumentEditComponent,
  DocumentsFoldersComponent
} from '../';
import {
  DocumentModel, DocumentSimpleFolderModel, DocumentSimpleModel,
} from '../../../../models';
import {CommonDictionaryModel, Paged} from 'src/app/common/models';
import {LocaleService} from 'src/app/common/services';
import {Subscription} from 'rxjs';
import {DocumentsStateService} from '../../../documents/store';
import {MatDialog} from '@angular/material/dialog';
import {Overlay} from '@angular/cdk/overlay';
import {dialogConfigHelper} from 'src/app/common/helpers';
import {applicationLanguagesTranslated} from 'src/app/common/const';
import {
  BackendConfigurationPnDocumentsService,
  BackendConfigurationPnPropertiesService
} from 'src/app/plugins/modules/backend-configuration-pn/services';
import {TranslateService} from '@ngx-translate/core';

@Component({
  selector: 'app-documents-container',
  templateUrl: './documents-container.component.html',
  styleUrls: ['./documents-container.component.scss'],
})
export class DocumentsContainerComponent implements OnInit, OnDestroy {
  folders: DocumentSimpleFolderModel[];
  documents: Paged<DocumentModel> = new Paged<DocumentModel>();
  simpleDocuments: DocumentSimpleModel[];
  documentDeletedSub$: Subscription;
  documentUpdatedSub$: Subscription;
  documentCreatedSub$: Subscription;
  folderManageModalClosedSub$: Subscription;
  selectedLanguage: number;
  properties: CommonDictionaryModel[] = [];

  constructor(
    private propertyService: BackendConfigurationPnPropertiesService,
    public backendConfigurationPnDocumentsService: BackendConfigurationPnDocumentsService,
    public dialog: MatDialog,
    private overlay: Overlay,
    private translate: TranslateService,
    public documentsStateService: DocumentsStateService,
    public localeService: LocaleService) {
    this.selectedLanguage = applicationLanguagesTranslated.find(
      (x) => x.locale === localeService.getCurrentUserLocale()
    ).id;
  }

  ngOnInit(): void {
    this.getProperties();
    //this.getFolders();
  }

  ngOnDestroy(): void {
  }

  openCreateModal() {
    const createDocumentModal = this.dialog.open(DocumentsDocumentCreateComponent, {...dialogConfigHelper(this.overlay), minWidth: 800});
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
    const editDocumentModal = this.dialog
      .open(DocumentsDocumentEditComponent, {...dialogConfigHelper(this.overlay, documentModel), minWidth: 800});
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
    this.backendConfigurationPnDocumentsService.getSimpleFolders(this.selectedLanguage).subscribe((data) => {
      if (data && data.success) {
        this.folders = data.model;
        this.documentsStateService.getDocuments().subscribe((data) => {
          if (data && data.success && data.model) {
            this.documents = data.model;
          }
        });
      }
    });
  }

  updateTable() {
    this.getProperties();
  }

  getDocumentsByFolderId(folderId: number) {
    return this.documents.entities.filter(x => x.folderId === folderId);
  }

  getProperties() {
    this.propertyService.getAllProperties({
      nameFilter: '',
      sort: 'Id',
      isSortDsc: false,
      pageSize: 100000,
      offset: 0,
      pageIndex: 0
    }).subscribe((data) => {
      if (data && data.success && data.model) {
        this.properties = [{id: -1, name: this.translate.instant('All'), description: ''}, ...data.model.entities
          .map((x) => {
            return {name: `${x.cvr ? x.cvr : ''} - ${x.chr ? x.chr : ''} - ${x.name}`, description: '', id: x.id};
          })];
        this.getFolders();
        this.getDocuments();
      }
    });
  }

  getDocuments(selectedPropertyId?: number) {
    this.backendConfigurationPnDocumentsService.getSimpleDocuments(this.selectedLanguage, selectedPropertyId).subscribe((data) => {
      if (data && data.success) {
        this.simpleDocuments = data.model;
      }
    });
  }
}
