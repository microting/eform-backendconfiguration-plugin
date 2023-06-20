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
import {Subscription, zip} from 'rxjs';
import {DocumentsStateService} from '../../../documents/store';
import {MatDialog} from '@angular/material/dialog';
import {Overlay} from '@angular/cdk/overlay';
import {dialogConfigHelper} from 'src/app/common/helpers';
import {applicationLanguagesTranslated} from 'src/app/common/const';
import {
  BackendConfigurationPnDocumentsService,
  BackendConfigurationPnPropertiesService
} from '../../../../services';
import {TranslateService} from '@ngx-translate/core';
import {skip, tap} from 'rxjs/operators';

@Component({
  selector: 'app-documents-container',
  templateUrl: './documents-container.component.html',
  styleUrls: ['./documents-container.component.scss'],
})
export class DocumentsContainerComponent implements OnInit, OnDestroy {
  folders: DocumentSimpleFolderModel[];
  documents: Paged<DocumentModel> = new Paged<DocumentModel>();
  simpleDocuments: DocumentSimpleModel[];
  selectedLanguage: number;
  properties: CommonDictionaryModel[] = [];

  documentDeletedSub$: Subscription;
  documentUpdatedSub$: Subscription;
  documentCreatedSub$: Subscription;
  folderManageModalClosedSub$: Subscription;
  getActiveSortDirectionSub$: Subscription;

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
    this.getActiveSortDirectionSub$ = this.documentsStateService.getActiveSortDirection()
      .pipe(
        skip(1), // skip initial value
        tap(() => {
          this.updateTable();
        }),
      )
      .subscribe();
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
      this.getFoldersAndDocuments();
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

  getFoldersAndDocuments() {
    zip(
      this.backendConfigurationPnDocumentsService.getSimpleFolders(this.selectedLanguage),
      this.documentsStateService.getDocuments(),
    ).subscribe(([folders, documents]) => {
      if (folders && folders.success && folders.model) {
        this.folders = folders.model;
      }
      if (documents && documents.success && documents.model) {
        this.documents = documents.model;
      }
    });
  }

  updateTable() {
    this.getProperties();
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
        this.getFoldersAndDocuments();
        this.getSimpleDocuments();
      }
    });
  }

  getSimpleDocuments(selectedPropertyId?: number) {
    this.backendConfigurationPnDocumentsService.getSimpleDocuments(this.selectedLanguage, selectedPropertyId).subscribe((data) => {
      if (data && data.success) {
        this.simpleDocuments = data.model;
      }
    });
  }
}
