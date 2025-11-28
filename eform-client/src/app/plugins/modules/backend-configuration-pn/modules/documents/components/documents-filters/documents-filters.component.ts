import {
  Component,
  EventEmitter, Input,
  OnDestroy,
  OnInit,
  Output, inject} from '@angular/core';
import {FormControl, FormGroup} from '@angular/forms';
import {AutoUnsubscribe} from 'ngx-auto-unsubscribe';
import {Subscription} from 'rxjs';
import {CommonDictionaryModel} from 'src/app/common/models';
import {DocumentSimpleFolderModel, DocumentSimpleModel} from '../../../../models';
import {DocumentsExpirationFilterEnum} from '../../../../enums';
import {Store} from '@ngrx/store';
import {
  selectDocumentsFilters,
  DocumentsFiltrationModel,
  updateDocumentsFilters
} from '../../../../state';

@AutoUnsubscribe()
@Component({
    selector: 'app-documents-filters',
    templateUrl: './documents-filters.component.html',
    styleUrls: ['./documents-filters.component.scss'],
    standalone: false
})
export class DocumentsFiltersComponent implements OnInit, OnDestroy {
  private store = inject(Store);

  @Output() updateTable: EventEmitter<void> = new EventEmitter<void>();
  @Input() folders: DocumentSimpleFolderModel[];
  @Input() documents: DocumentSimpleModel[];
  @Input() properties: CommonDictionaryModel[] = [];
  filtersForm: FormGroup;
  currentFilters: DocumentsFiltrationModel;

  selectFiltersSub$: Subscription;
  propertyIdValueChangesSub$: Subscription;
  folderNameChangesSub$: Subscription;
  documentChangesSub$: Subscription;
  expireChangesSub$: Subscription;
  selectDocumentsFilters$ = this.store.select(selectDocumentsFilters);

  

  ngOnInit(): void {
    this.selectFiltersSub$ = this.selectDocumentsFilters$
      .subscribe((filters) => {
        this.currentFilters = filters;
        if (!this.filtersForm) {
          this.filtersForm = new FormGroup({
            propertyId: new FormControl(filters.propertyId || -1),
            folderId: new FormControl(filters.folderId),
            documentId: new FormControl(filters.documentId),
            expiration: new FormControl(filters.expiration),
          });
          if (filters.propertyId === null) {
            this.store.dispatch(updateDocumentsFilters({
              ...filters,
              propertyId: -1,
            }));
          }
          if (filters.propertyId && filters.propertyId !== -1) {
            //this.getDocuments(filters.propertyId);
            //this.getSites(filters.propertyId);
          }
        }
      });
    this.propertyIdValueChangesSub$ = this.filtersForm
      .get('propertyId')
      .valueChanges.subscribe((value: number) => {
        if (this.currentFilters.propertyId !== value) {
          this.store.dispatch(updateDocumentsFilters({
            ...this.currentFilters,
            propertyId: value,
          }));
        }
      });
    this.folderNameChangesSub$ = this.filtersForm
      .get('folderId')
      .valueChanges.subscribe((value: string) => {
        if (this.currentFilters.folderId !== value) {
          this.store.dispatch(updateDocumentsFilters({
            ...this.currentFilters,
            folderId: value,
          }));
        }
      });
    this.documentChangesSub$ = this.filtersForm
      .get('documentId')
      .valueChanges.subscribe((value: string) => {
        if (this.currentFilters.documentId !== value) {
          this.store.dispatch(updateDocumentsFilters({
            ...this.currentFilters,
            documentId: value,
          }));
        }
      });
    this.expireChangesSub$ = this.filtersForm
      .get('expiration')
      .valueChanges.subscribe((value: DocumentsExpirationFilterEnum) => {
        if (this.currentFilters.expiration !== value) {
          this.store.dispatch(updateDocumentsFilters({
            ...this.currentFilters,
            expiration: value,
          }));
        }
      });
  }

  ngOnDestroy(): void {
  }
}
