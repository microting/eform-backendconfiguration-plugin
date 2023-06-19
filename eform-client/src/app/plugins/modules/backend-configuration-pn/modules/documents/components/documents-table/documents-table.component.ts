import {
  Component,
  EventEmitter,
  Input, OnDestroy,
  OnInit,
  Output,
} from '@angular/core';
import {DocumentModel, DocumentSimpleFolderModel, DocumentTranslationModel,} from '../../../../models';
import {MtxGridColumn} from '@ng-matero/extensions/grid';
import {TranslateService} from '@ngx-translate/core';
import {applicationLanguages2, PdfIcon, WordIcon} from 'src/app/common/const';
import {MatIconRegistry} from '@angular/material/icon';
import {DomSanitizer} from '@angular/platform-browser';
import {TemplateFilesService} from 'src/app/common/services';
import {Subscription} from 'rxjs';
import {AutoUnsubscribe} from 'ngx-auto-unsubscribe';
import {DocumentsStateService} from '../../store';
import {Sort} from '@angular/material/sort';

@AutoUnsubscribe()
@Component({
  selector: 'app-documents-table',
  templateUrl: './documents-table.component.html',
  styleUrls: ['./documents-table.component.scss'],
})
export class DocumentsTableComponent implements OnInit, OnDestroy {
  tableHeaders: MtxGridColumn[] = [
    {field: 'id', header: this.translateService.stream('Id'), sortable: true, sortProp: {id: 'Id'}},
    {
      field: 'propertyNames',
      header: this.translateService.stream('Properties'),
      formatter: (document: DocumentModel) => document.propertyNames
    },
    {
      field: 'propertyFolder',
      header: this.translateService.stream('Folder name'),
      formatter: (document: DocumentModel) => this.folders.filter(x => x.id === document.folderId).map(x => x.name)[0] || '--',
      sortable: true, sortProp: {id: 'FolderId'}
    },
    {
      field: 'documentTranslations[0].name',
      header: this.translateService.stream('Document name'),
      formatter: (document: DocumentModel) => this.getDocumentTranslationName(document),
    },
    {
      field: 'documentTranslations[0].description',
      header: this.translateService.stream('Document description'),
      formatter: (document: DocumentModel) => this.getDocumentTranslationDescription(document)
    },
    {
      field: 'endDate',
      header: this.translateService.stream('End date'),
      type: 'date',
      typeParameter: {format: 'dd.MM.y'},
      sortable: true, sortProp: {id: 'EndAt'}
    },
    // {
    //   field: 'downloadButton',
    //   header: this.translateService.stream('Files'),
    // },
    {
      field: 'status',
      header: this.translateService.stream('Status'),
      formatter: (document: DocumentModel) => this.translateService.instant(document.status ? 'ON' : 'OFF'),
      sortable: true, sortProp: {id: 'Status'}
    },
    {
      field: 'actions',
      header: this.translateService.stream('Actions'),
      type: 'button',
      width: '160px',
      pinned: 'right',
      right: '0px',
      buttons: [
        {
          type: 'icon',
          icon: 'edit',
          tooltip: this.translateService.stream('Edit document'),
          iif: (document: DocumentModel) => document.isLocked === false,
          click: (document: DocumentModel) => this.onShowEditDocumentModal(document),
          class: 'editDocumentBtn',
        },
        {
          color: 'warn',
          type: 'icon',
          icon: 'delete',
          tooltip: this.translateService.stream('Delete document'),
          iif: (document: DocumentModel) => document.isLocked === false,
          click: (document: DocumentModel) => this.onOpenDeleteModal(document),
          class: 'deleteDocumentBtn',
        },
      ],
    },
  ];
  @Input() documents: DocumentModel[] = [];
  @Input() folders: DocumentSimpleFolderModel[] = [];
  @Output() updateTable: EventEmitter<void> = new EventEmitter<void>();
  @Output() openViewModal: EventEmitter<number> = new EventEmitter<number>();
  @Output() openDeleteModal: EventEmitter<DocumentModel> = new EventEmitter<DocumentModel>();
  @Output() openEditModal: EventEmitter<DocumentModel> = new EventEmitter<DocumentModel>();
  pdfSub$: Subscription;

  constructor(
    private templateFilesService: TemplateFilesService,
    private translateService: TranslateService,
    iconRegistry: MatIconRegistry,
    sanitizer: DomSanitizer,
    public documentsStateService: DocumentsStateService,
  ) {
    iconRegistry.addSvgIconLiteral('file-word', sanitizer.bypassSecurityTrustHtml(WordIcon));
    iconRegistry.addSvgIconLiteral('file-pdf', sanitizer.bypassSecurityTrustHtml(PdfIcon));
  }

  getLanguageById(languageId: number) {
    const index = applicationLanguages2.findIndex(x => x.id === languageId);
    if (index !== -1) {
      return applicationLanguages2[index];
    }
    return applicationLanguages2[0];
  }

  ngOnInit(): void {
  }

  onOpenViewModal(id: number) {
    this.openViewModal.emit(id);
  }

  onOpenDeleteModal(documentModel: DocumentModel) {
    this.openDeleteModal.emit(documentModel);
  }

  onShowEditDocumentModal(propertyModel: DocumentModel) {
    this.openEditModal.emit(propertyModel);
  }

  getDocumentTranslationName(documentModel: DocumentModel) {
    if (documentModel.documentTranslations[0].name) {
      return documentModel.documentTranslations[0].name;
    } else if (documentModel.documentTranslations.some(x => x.name)) {
      return documentModel.documentTranslations.filter(x => x.name)[0].name;
    }
    return '';
  }

  getDocumentTranslationDescription(documentModel: DocumentModel) {
    const emptyString = '<div></div>';
    const predicate = (value: DocumentTranslationModel) => value.description && value.description !== emptyString;
    if (documentModel.documentTranslations[0].description && documentModel.documentTranslations[0].description !== emptyString) {
      return documentModel.documentTranslations[0].description;
    } else if (documentModel.documentTranslations.some(predicate)) {
      return documentModel.documentTranslations.filter(predicate)[0].description;
    }
    return '';
  }

  getFile(indexDocument: number, languageId: number, extension: string = 'pdf') {
    const index = this.documents[indexDocument].documentUploadedDatas
      .findIndex((x) => x.languageId === languageId && x.extension === extension);
    if (index !== -1) {
      const documentUploadedData = this.documents[indexDocument].documentUploadedDatas[index];
      if (documentUploadedData.id) {
        this.pdfSub$ = this.templateFilesService.getImage(documentUploadedData.fileName)
          .subscribe((blob) => {
            const fileURL = URL.createObjectURL(blob);
            window.open(fileURL, '_blank');
          });
      } else {
        window.open(URL.createObjectURL(documentUploadedData.file), '_blank');
      }
    }
  }

  ngOnDestroy(): void {
  }

  onSortChange(sort: Sort) {
    this.documentsStateService.onSortTable(sort.active);
  }
}
