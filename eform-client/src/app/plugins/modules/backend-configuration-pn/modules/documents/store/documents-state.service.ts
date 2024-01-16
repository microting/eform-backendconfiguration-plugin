import {Injectable} from '@angular/core';
import {Observable} from 'rxjs';
import {
  CommonPaginationState,
  OperationDataResult,
  Paged,
} from 'src/app/common/models';
import {updateTableSort} from 'src/app/common/helpers';
import {
  DocumentFolderModel,
  DocumentModel,
} from '../../../models';
import {BackendConfigurationPnDocumentsService, BackendConfigurationPnPropertiesService} from '../../../services';
import {updateDocumentsPagination, selectDocumentsFilters, selectDocumentsPagination, DocumentsFiltrationModel} from '../../../state';
import {Store} from '@ngrx/store';

@Injectable({providedIn: 'root'})
export class DocumentsStateService {
  private selectDocumentsFilters$ = this.store.select(selectDocumentsFilters);
  private selectDocumentsPagination$ = this.store.select(selectDocumentsPagination);
  currentPagination: CommonPaginationState;
  currentFilters: DocumentsFiltrationModel;

  constructor(
    private store: Store,
    private service: BackendConfigurationPnPropertiesService,
    public backendConfigurationPnDocumentsService: BackendConfigurationPnDocumentsService
  ) {
    this.selectDocumentsPagination$.subscribe(x => this.currentPagination = x);
    this.selectDocumentsFilters$.subscribe(x => this.currentFilters = x);
  }

  getFolders(): Observable<OperationDataResult<Paged<DocumentFolderModel>>> {
    return this.backendConfigurationPnDocumentsService.getAllFolders({
      documentId: this.currentFilters.documentId,
      expiration: this.currentFilters.expiration,
      propertyId: this.currentFilters.propertyId,
      folderId: this.currentFilters.folderId
    });
  }

  getDocuments(): Observable<OperationDataResult<Paged<DocumentModel>>> {
    return this.backendConfigurationPnDocumentsService.getAllDocuments({
      ...this.currentFilters,
      propertyId: this.currentFilters.propertyId === null ? -1 : this.currentFilters.propertyId,
      ...this.currentPagination,
    });
  }

  // getAllProperties(): Observable<OperationDataResult<Paged<PropertyModel>>> {
  //   return this.service
  //     .getAllProperties({
  //       ...this.query.pageSetting.pagination,
  //       ...this.query.pageSetting.filters,
  //       pageIndex: 0,
  //     })
  //     .pipe(
  //       map((response) => {
  //         if (response && response.success && response.model) {
  //           this.store.update(() => ({
  //             totalProperties: response.model.total,
  //           }));
  //         }
  //         return response;
  //       })
  //     );
  // }

  onSortTable(sort: string) {
    const localPageSettings = updateTableSort(
      sort,
      this.currentPagination.sort,
      this.currentPagination.isSortDsc
    );
    this.store.dispatch(updateDocumentsPagination({
      ...this.currentPagination,
      isSortDsc: localPageSettings.isSortDsc,
      sort: localPageSettings.sort,
    }));
  }
}
