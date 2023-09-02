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
import {LocaleService} from 'src/app/common/services';
import {TranslateService} from '@ngx-translate/core';
import {DateTimeAdapter} from '@danielmoncada/angular-datetime-picker';
import {AuthStateService} from 'src/app/common/store';
import {DocumentSimpleFolderModel, DocumentSimpleModel} from '../../../../models';
import {applicationLanguagesTranslated} from 'src/app/common/const';
import {DocumentsStateService} from '../../store';
import {DocumentsExpirationFilterEnum} from '../../../../enums';

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

  constructor(
    dateTimeAdapter: DateTimeAdapter<any>,
    private translate: TranslateService,
    public documentsStateService: DocumentsStateService,
    localeService: LocaleService,
    authStateService: AuthStateService
  ) {
    this.selectedLanguage = applicationLanguagesTranslated.find(
      (x) => x.locale === localeService.getCurrentUserLocale()
    ).id;
    dateTimeAdapter.setLocale(authStateService.currentUserLocale);
  }

  ngOnInit(): void {
    //this.getProperties();
    this.selectFiltersSub$ = this.documentsStateService
      .getFiltersAsync()
      .subscribe((filters) => {
        if (!this.filtersForm) {
          this.filtersForm = new FormGroup({
            propertyId: new FormControl(filters.propertyId || -1),
            folderId: new FormControl(filters.folderId),
            documentId: new FormControl(filters.documentId),
            expiration: new FormControl(filters.expiration),
          });
          if (filters.propertyId === null) {
            this.documentsStateService.store.update((state) => ({
              filters: {
                ...state.filters,
                propertyId: -1,
              },
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
        if (
          this.documentsStateService.store.getValue().filters
            .propertyId !== value
        ) {
          if (value !== -1) { /* empty */
          } else { /* empty */
          }
          this.documentsStateService.store.update((state) => ({
            filters: {
              ...state.filters,
              propertyId: value,
            },
          }));
        }
      });
    this.folderNameChangesSub$ = this.filtersForm
      .get('folderId')
      .valueChanges.subscribe((value: string) => {
        if (
          this.documentsStateService.store.getValue().filters.folderId !==
          value
        ) {
          this.documentsStateService.store.update((state) => ({
            filters: {
              ...state.filters,
              folderId: value,
            },
          }));
        }
      });
    this.documentChangesSub$ = this.filtersForm
      .get('documentId')
      .valueChanges.subscribe((value: string) => {
        if (this.documentsStateService.store.getValue().filters.documentId !== value) {
          this.documentsStateService.store.update((state) => ({
            filters: {
              ...state.filters,
              documentId: value,
            },
          }));
        }
      });
    this.expireChangesSub$ = this.filtersForm
      .get('expiration')
      .valueChanges.subscribe((value: DocumentsExpirationFilterEnum) => {
        if (this.documentsStateService.store.getValue().filters.expiration !== value) {
          this.documentsStateService.store.update((state) => ({
            filters: {
              ...state.filters,
              expiration: value,
            },
          }));
        }
      });
  }

  ngOnDestroy(): void {
  }
}
