import {
  Component,
  EventEmitter, Input,
  OnDestroy,
  OnInit,
  Output,
} from '@angular/core';
import {FormControl, FormGroup} from '@angular/forms';
import {AutoUnsubscribe} from 'ngx-auto-unsubscribe';
import {Subscription} from 'rxjs';
import {CommonDictionaryModel} from 'src/app/common/models';
import {TranslateService} from '@ngx-translate/core';
import {DateTimeAdapter} from '@danielmoncada/angular-datetime-picker';
import {DocumentSimpleFolderModel, DocumentSimpleModel} from '../../../../models';
import {applicationLanguagesTranslated} from 'src/app/common/const';
import {DocumentsStateService} from '../../store';
import {DocumentsExpirationFilterEnum} from '../../../../enums';
import {selectCurrentUserLocale} from 'src/app/state/auth/auth.selector';
import {Store} from '@ngrx/store';
import {
  selectDocumentsFilters
} from '../../../../state/documents/documents.selector';
import {
  DocumentsFiltrationModel
} from '../../../../state/documents/documents.reducer';

@AutoUnsubscribe()
@Component({
  selector: 'app-documents-filters',
  templateUrl: './documents-filters.component.html',
  styleUrls: ['./documents-filters.component.scss'],
})
export class DocumentsFiltersComponent implements OnInit, OnDestroy {
  @Output() updateTable: EventEmitter<void> = new EventEmitter<void>();
  @Input() folders: DocumentSimpleFolderModel[];
  @Input() documents: DocumentSimpleModel[];
  @Input() properties: CommonDictionaryModel[] = [];
  filtersForm: FormGroup;
  selectedLanguage: number;

  selectFiltersSub$: Subscription;
  propertyIdValueChangesSub$: Subscription;
  folderNameChangesSub$: Subscription;
  documentChangesSub$: Subscription;
  expireChangesSub$: Subscription;
  private selectCurrentUserLocale$ = this.store.select(selectCurrentUserLocale);
  private selectDocumentsFilters$ = this.store.select(selectDocumentsFilters);

  constructor(
    dateTimeAdapter: DateTimeAdapter<any>,
    private store: Store,
    private translate: TranslateService,
    public documentsStateService: DocumentsStateService,
  ) {
    this.selectCurrentUserLocale$.subscribe((locale) => {
      dateTimeAdapter.setLocale(locale);
      this.selectedLanguage = applicationLanguagesTranslated.find(
        (x) => x.locale === locale
      ).id;
    });
  }

  ngOnInit(): void {
    this.selectFiltersSub$ = this.selectDocumentsFilters$
      .subscribe((filters) => {
        if (!this.filtersForm) {
          this.filtersForm = new FormGroup({
            propertyId: new FormControl(filters.propertyId || -1),
            folderId: new FormControl(filters.folderId),
            documentId: new FormControl(filters.documentId),
            expiration: new FormControl(filters.expiration),
          });
          if (filters.propertyId === null) {
            this.store.dispatch({
              type: '[Documents] Update filters',
              payload: {
                propertyId: -1,
              }
            });
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
        let currentFilters: DocumentsFiltrationModel;
        this.selectDocumentsFilters$.subscribe((filters) => {
          currentFilters = filters;
        }).unsubscribe();
        if (currentFilters.propertyId !== value) {
          this.store.dispatch({
            type: '[Documents] Update filters',
            payload: {
              filters: {
                ...currentFilters,
                propertyId: value,
              }
            }
          });
        }
      });
    this.folderNameChangesSub$ = this.filtersForm
      .get('folderId')
      .valueChanges.subscribe((value: string) => {
        let currentFilters: DocumentsFiltrationModel;
        this.selectDocumentsFilters$.subscribe((filters) => {
          currentFilters = filters;
        }).unsubscribe();
        if (currentFilters.folderId !== value) {
          this.store.dispatch({
            type: '[Documents] Update filters',
            payload: {
              filters: {
                ...currentFilters,
                folderId: value,
              }
            }
          });
        }
      });
    this.documentChangesSub$ = this.filtersForm
      .get('documentId')
      .valueChanges.subscribe((value: string) => {
        let currentFilters: DocumentsFiltrationModel;
        this.selectDocumentsFilters$.subscribe((filters) => {
          currentFilters = filters;
        }).unsubscribe();
        if (currentFilters.documentId !== value) {
          this.store.dispatch({
            type: '[Documents] Update filters',
            payload: {
              filters: {
                ...currentFilters,
                documentId: value,
              }
            }
          });
        }
      });
    this.expireChangesSub$ = this.filtersForm
      .get('expiration')
      .valueChanges.subscribe((value: DocumentsExpirationFilterEnum) => {
        let currentFilters: DocumentsFiltrationModel;
        this.selectDocumentsFilters$.subscribe((filters) => {
          currentFilters = filters;
        }).unsubscribe();
        if (currentFilters.expiration !== value) {
          this.store.dispatch({
            type: '[Documents] Update filters',
            payload: {
              filters: {
                ...currentFilters,
                expiration: value,
              }
            }
          });
        }
      });
  }

  ngOnDestroy(): void {
  }
}
