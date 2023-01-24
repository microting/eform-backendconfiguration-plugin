import {
  Component,
  EventEmitter,
  Input,
  OnInit,
  Output,
} from '@angular/core';
import {FilesModel} from '../../../../models';
import {MtxGridColumn} from '@ng-matero/extensions/grid';
import {TranslateService} from '@ngx-translate/core';
import {Paged, SharedTagModel} from 'src/app/common/models';
import {FilesStateService} from '../../store';
import {Sort} from '@angular/material/sort';

@Component({
  selector: 'app-files-table',
  templateUrl: './files-table.component.html',
  styleUrls: ['./files-table.component.scss'],
})
export class FilesTableComponent implements OnInit {
  @Input() files: Paged<FilesModel> = new Paged<FilesModel>();
  @Output() updateTable: EventEmitter<void> = new EventEmitter<void>();
  @Output() openViewModal: EventEmitter<number> = new EventEmitter<number>();
  @Output() openDeleteModal: EventEmitter<FilesModel> = new EventEmitter<FilesModel>();
  @Output() openEditNameModal: EventEmitter<FilesModel> = new EventEmitter<FilesModel>();
  @Output() openEditTagsModal: EventEmitter<FilesModel> = new EventEmitter<FilesModel>();
  @Output() clickTag: EventEmitter<number> = new EventEmitter<number>();
  tableHeaders: MtxGridColumn[] = [
    {field: 'id', header: this.translateService.stream('Id'), sortable: true, sortProp: {id: 'Id'}},
    {
      field: 'createDate',
      header: this.translateService.stream('Create date'),
      type: 'date',
      typeParameter: {format: 'dd.MM.yyyy HH:mm:ss'},
      sortable: true,
      sortProp: {id: 'CreatedAt'}
    },
    {
      field: 'fileName',
      header: this.translateService.stream('Filename'),
      sortable: true,
      sortProp: {id: 'FileName'},
      formatter: (filesModel: FilesModel) => `${filesModel.fileName}.${filesModel.fileExtension}`
    },
    {field: 'property', header: this.translateService.stream('Property'), sortable: true, sortProp: {id: 'PropertyId'}},
    {field: 'tags', header: this.translateService.stream('Tags')},
    {
      field: 'actions',
      header: this.translateService.stream('Actions'),
      type: 'button',
      width: '200px',
      pinned: 'right',
      right: '0px',
      buttons: [
        {
          type: 'icon',
          icon: 'visibility',
          tooltip: this.translateService.stream('View PDF'),
          click: (filesModel: FilesModel) => this.onOpenView(filesModel.id),
          class: 'viewPdfBtn',
        },
        {
          color: 'accent',
          type: 'icon',
          icon: 'edit',
          tooltip: this.translateService.stream('Edit filename'),
          click: (filesModel: FilesModel) => this.onShowEditDocumentModal(filesModel),
          class: 'editFilenameBtn',
        },
        {
          color: 'warn',
          type: 'icon',
          icon: 'delete',
          tooltip: this.translateService.stream('Delete file'),
          click: (filesModel: FilesModel) => this.onOpenDeleteModal(filesModel),
          class: 'deleteFileBtn',
        },
      ],
    },
  ];

  constructor(
    public filesStateService: FilesStateService,
    private translateService: TranslateService,
  ) {
  }

  ngOnInit(): void {
  }

  onOpenView(id: number) {
    this.openViewModal.emit(id);
  }

  onOpenDeleteModal(model: FilesModel) {
    this.openDeleteModal.emit(model);
  }

  onShowEditDocumentModal(model: FilesModel) {
    this.openEditNameModal.emit(model);
  }

  openEditTags(model: FilesModel) {
    this.openEditTagsModal.emit(model);
  }

  onSortTable(sort: Sort) {
    this.filesStateService.onSortTable(sort.active);
    this.updateTable.emit();
  }

  onClickTag(tag: SharedTagModel) {
    this.clickTag.emit(tag.id);
  }
}
