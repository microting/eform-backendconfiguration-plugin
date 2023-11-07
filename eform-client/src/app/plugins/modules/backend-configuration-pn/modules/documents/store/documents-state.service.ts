import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import {
  CommonPaginationState,
  OperationDataResult,
  Paged,
} from 'src/app/common/models';
import { updateTableSort } from 'src/app/common/helpers';
import {
  DocumentFolderModel,
  DocumentModel,
} from '../../../models';
import {BackendConfigurationPnDocumentsService, BackendConfigurationPnPropertiesService} from '../../../services';
import {Store} from '@ngrx/store';
import {
  selectDocumentsFilters, selectDocumentsPagination
} from '../../../state/documents/documents.selector';

@Injectable({ providedIn: 'root' })
export class DocumentsStateService {
  private selectDocumentsFilters$ = this.store.select(selectDocumentsFilters);
  private selectDocumentsPagination$ = this.store.select(selectDocumentsPagination);
  constructor(
      private store: Store,
    private service: BackendConfigurationPnPropertiesService,
    public backendConfigurationPnDocumentsService: BackendConfigurationPnDocumentsService
  ) {}

  getFolders() : Observable<OperationDataResult<Paged<DocumentFolderModel>>> {
    let _filters:any;
    this.selectDocumentsFilters$.subscribe((filters) => {
      _filters = filters;
    }).unsubscribe();
    return this.backendConfigurationPnDocumentsService.getAllFolders({
      documentId: _filters.documentId,
      expiration: _filters.expiration,
      propertyId: _filters.propertyId,
      folderId: _filters.folderId});
  }

  getDocuments() : Observable<OperationDataResult<Paged<DocumentModel>>> {
    let _filters:any;
    this.selectDocumentsFilters$.subscribe((filters) => {
      _filters = filters;
    }).unsubscribe();
    let _pagination: CommonPaginationState;
    this.selectDocumentsPagination$.subscribe((pagination) => {
      _pagination = pagination;
    }).unsubscribe();
    return this.backendConfigurationPnDocumentsService.getAllDocuments({
        documentId: _filters.documentId,
        expiration: _filters.expiration,
        propertyId: _filters.propertyId,
        folderId: _filters.folderId,
      sort: _pagination.sort,
      isSortDsc: _pagination.isSortDsc,
    });
  }

  // getNameFilter(): Observable<string> {
  //   return this.query.selectNameFilter$;
  // }

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


  // getFiltersAsync(): Observable<DocumentsFiltrationModel> {
  //   return this.query.selectFilters$;
  // }

  updateNameFilter(nameFilter: string) {
    // this.store.update((state) => ({
    //   filters: {
    //     ...state.filters,
    //     nameFilter: nameFilter,
    //   },
    //   pagination: {
    //     ...state.pagination,
    //     offset: 0,
    //   },
    // }));
  }

  // updatePageSize(pageSize: number) {
  //   this.store.update((state) => ({
  //     pagination: {
  //       ...state.pagination,
  //       pageSize: pageSize,
  //     },
  //   }));
  //   this.checkOffset();
  // }

  changePage(offset: number) {
    // this.store.update((state) => ({
    //   pagination: {
    //     ...state.pagination,
    //     offset: offset,
    //   },
    // }));
  }

  // onDelete() {
  //   this.store.update((state) => ({
  //     totalProperties: state.totalProperties - 1,
  //   }));
  //   this.checkOffset();
  // }

  onSortTable(sort: string) {
    let currentPagination: CommonPaginationState;
    this.selectDocumentsPagination$.subscribe((pagination) => {
      currentPagination = pagination;
    }).unsubscribe();
    const localPageSettings = updateTableSort(
      sort,
      currentPagination.sort,
      currentPagination.isSortDsc
    );
    this.store.dispatch({
      type: '[Documents] Update pagination',
      payload: {
        ...currentPagination,
        isSortDsc: localPageSettings.isSortDsc,
        sort: localPageSettings.sort,
      }
    });
    // const localPageSettings = updateTableSort(
    //   sort,
    //   this.query.pageSetting.pagination.sort,
    //   this.query.pageSetting.pagination.isSortDsc
    // );
    // this.store.update((state) => ({
    //   pagination: {
    //     ...state.pagination,
    //     isSortDsc: localPageSettings.isSortDsc,
    //     sort: localPageSettings.sort,
    //   },
    // }));
  }

  // checkOffset() {
  //   const newOffset = getOffset(
  //     this.query.pageSetting.pagination.pageSize,
  //     this.query.pageSetting.pagination.offset,
  //     this.query.pageSetting.totalProperties
  //   );
  //   if (newOffset !== this.query.pageSetting.pagination.offset) {
  //     this.store.update((state) => ({
  //       pagination: {
  //         ...state.pagination,
  //         offset: newOffset,
  //       },
  //     }));
  //   }
  // }
  //
  // getPagination(): Observable<PaginationModel> {
  //   return this.query.selectPagination$;
  // }
}
