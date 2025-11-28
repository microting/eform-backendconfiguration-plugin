import {Component, OnDestroy, OnInit, inject} from '@angular/core';
import {
  DocumentsDocumentCreateComponent,
  DocumentsDocumentDeleteComponent,
  DocumentsDocumentEditComponent,
  DocumentsFoldersComponent
} from '../';
import {
  DocumentModel,
  DocumentSimpleFolderModel,
  DocumentSimpleModel,
  DocumentUpdatedDaysModel,
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
  BackendConfigurationPnPropertiesService,
} from '../../../../services';
import {TranslateService} from '@ngx-translate/core';
import {skip, tap} from 'rxjs/operators';
import {ActivatedRoute} from '@angular/router';
import {StatisticsStateService} from '../../../statistics/store';
import {selectCurrentUserLanguageId} from 'src/app/state/auth/auth.selector';
import {Store} from '@ngrx/store';
import {
  selectDocumentsFilters,
  selectDocumentsFiltersPropertyId, selectDocumentsPaginationIsSortDsc
} from '../../../../state/documents/documents.selector';

@Component({
    selector: 'app-documents-container',
    templateUrl: './documents-container.component.html',
    styleUrls: ['./documents-container.component.scss'],
    standalone: false
})
export class DocumentsContainerComponent implements OnInit, OnDestroy {
  private store = inject(Store);
  private propertyService = inject(BackendConfigurationPnPropertiesService);
  public backendConfigurationPnDocumentsService = inject(BackendConfigurationPnDocumentsService);
  public dialog = inject(MatDialog);
  private overlay = inject(Overlay);
  private translate = inject(TranslateService);
  public documentsStateService = inject(DocumentsStateService);
  public localeService = inject(LocaleService);
  private route = inject(ActivatedRoute);
  private statisticsStateService = inject(StatisticsStateService);

  folders: DocumentSimpleFolderModel[];
  documents: Paged<DocumentModel> = new Paged<DocumentModel>();
  simpleDocuments: DocumentSimpleModel[];
  selectedLanguage: number;
  properties: CommonDictionaryModel[] = [];
  documentUpdatedDaysModel: DocumentUpdatedDaysModel;
  selectedPropertyId: number | null = null;
  view = [1000, 300];
  showDiagram: boolean = false;

  documentDeletedSub$: Subscription;
  documentUpdatedSub$: Subscription;
  documentCreatedSub$: Subscription;
  folderManageModalClosedSub$: Subscription;
  getActiveSortDirectionSub$: Subscription;
  getFiltersAsyncSub$: Subscription;
  getDocumentUpdatedDaysSub$: Subscription;
  private selectCurrentUserLanguageId$ = this.store.select(selectCurrentUserLanguageId);
  private selectDocumentsFiltersPropertyId$ = this.store.select(selectDocumentsFiltersPropertyId);
  private selectDocumentsPaginationIsSortDsc$ = this.store.select(selectDocumentsPaginationIsSortDsc);
  private selectDocumentsFilters$ = this.store.select(selectDocumentsFilters);

  get propertyName(): string {
    if (this.properties && this.selectedPropertyId) {
      const index = this.properties.findIndex(x => x.id === this.selectedPropertyId);
      if (index !== -1) {
        return this.properties[index].name;
      }
    }
    return '';
  }

  
  constructor() {
    this.selectCurrentUserLanguageId$.subscribe((languageId) => {
      this.selectedLanguage = languageId;
    });
    // this.selectedLanguage = applicationLanguagesTranslated.find(
    //   (x) => x.locale === localeService.getCurrentUserLocale()
    // ).id;
    this.route.queryParams.subscribe(x => {
      if (x && x.showDiagram) {
        this.showDiagram = x.showDiagram;
        this.selectDocumentsFiltersPropertyId$.subscribe((propertyId) => {
          this.selectedPropertyId = propertyId;
        });

        this.getDocumentUpdatedDays();
    //     this.selectedPropertyId = this.documentsStateService.store.getValue().filters.propertyId || null;
    //     this.getDocumentUpdatedDays();
      } else {
        this.showDiagram = false;
      }
    });
  }


  ngOnInit(): void {
    this.getProperties();
    this.getActiveSortDirectionSub$ = this.selectDocumentsPaginationIsSortDsc$
      .pipe(
        skip(1), // skip initial value
        tap(() => {
          this.updateTable();
        }),
      )
      .subscribe();
    // this.getFiltersAsyncSub$ = this.selectDocumentsFilters$
    //   .pipe(
    //     skip(1), // skip initial value
    //     tap(() => {
    //       if (this.showDiagram) {
    //         this.selectedPropertyId = this.documentsStateService.store.getValue().filters.propertyId || null;
    //         this.getDocumentUpdatedDays();
    //       }
    //     }),
    //   )
    //   .subscribe();
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
        // debugger;
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
        this.translate.stream('All').subscribe(allTranslate => {
          this.properties = [{id: -1, name: allTranslate, description: ''}, ...data.model.entities
            .map((x) => {
              return {name: `${x.name}`, description: '', id: x.id};
            })];
        })
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

  getDocumentUpdatedDays() {
    this.getDocumentUpdatedDaysSub$ = this.statisticsStateService.getDocumentUpdatedDays()
      .pipe(tap(model => {
        if (model && model.success && model.model) {
          this.documentUpdatedDaysModel = model.model;
        }
      }))
      .subscribe();
  }
}
