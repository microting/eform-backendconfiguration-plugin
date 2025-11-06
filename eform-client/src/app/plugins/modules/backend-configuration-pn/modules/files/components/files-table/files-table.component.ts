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
import {BackendConfigurationPnFilesService} from 'src/app/plugins/modules/backend-configuration-pn/services';
import {Store} from "@ngrx/store";
import {
  selectFilesNameFilters,
  selectFilesPaginationIsSortDsc,
  selectFilesPaginationSort
} from "src/app/plugins/modules/backend-configuration-pn/state/files/files.selector";

@Component({
    selector: 'app-files-table',
    templateUrl: './files-table.component.html',
    styleUrls: ['./files-table.component.scss'],
    standalone: false
})
export class FilesTableComponent implements OnInit {
  _files: Paged<FilesModel> = new Paged<FilesModel>();
  @Input()
  set files(files: Paged<FilesModel>) {
    this._files = files;
    this.selectedFiles = [];
    this.allFileSelected = false;
  }
  get files() {
    return this._files;
  }
  @Output() updateTable: EventEmitter<void> = new EventEmitter<void>();
  @Output() openDeleteModal: EventEmitter<FilesModel> = new EventEmitter<FilesModel>();
  @Output() openEditNameModal: EventEmitter<FilesModel> = new EventEmitter<FilesModel>();
  @Output() openEditTagsModal: EventEmitter<FilesModel> = new EventEmitter<FilesModel>();
  @Output() changeSelectedFiles: EventEmitter<number[]> = new EventEmitter<number[]>();
  @Output() clickTag: EventEmitter<number> = new EventEmitter<number>();

  selectedFiles: number[] = [];
  allFileSelected: boolean = false;

  tableHeaders: MtxGridColumn[] = [
    {field: 'select', header: '', width: '50px'},
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
      header: this.translateService.stream('File name'),
      sortable: true,
      sortProp: {id: 'FileName'},
      formatter: (filesModel: FilesModel) => `${filesModel.fileName}.${filesModel.fileExtension}`
    },
    {field: 'property', header: this.translateService.stream('Property'), sortable: false, sortProp: {id: 'PropertyId'}},
    {field: 'tags', header: this.translateService.stream('Tags')},
    {
      field: 'actions',
      header: this.translateService.stream('Actions'),
      width: '200px',
      pinned: 'right',
      right: '0px',
    },
  ];
  pdfSub$: any;

  get getIntermediateSelectedFiles() {
    return this.selectedFiles.length > 0 && this.selectedFiles.length !== this._files.total;
  }
  public selectFilesPaginationSort$ = this.store.select(selectFilesPaginationSort);
  public selectFilesPaginationIsSortDsc$ = this.store.select(selectFilesPaginationIsSortDsc);
  public selectFilesNameFilters$ = this.store.select(selectFilesNameFilters);

  constructor(
    private store: Store,
    public filesStateService: FilesStateService,
    private translateService: TranslateService,
    private filesService: BackendConfigurationPnFilesService,
  ) {
  }

  ngOnInit(): void {
  }

  onOpenView(file: FilesModel) {
    this.pdfSub$ = this.filesService.getPdfFile(file.id).subscribe((blob) => {
      const fileURL = URL.createObjectURL(blob);
      window.open(fileURL);
    });
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

  onChangeSelectedFiles(row: FilesModel) {
    const i = this.selectedFiles.findIndex(x => x === row.id);
    if (i !== -1) {
      this.selectedFiles = this.selectedFiles.filter(x => x !== row.id);
    } else {
      this.selectedFiles = [...this.selectedFiles, row.id];
    }
    if (this.selectedFiles.length === 0) {
      this.allFileSelected = false;
    } else if (this.selectedFiles.length !== this._files.total) {
      this.allFileSelected = true;
    }
    this.changeSelectedFiles.emit(this.selectedFiles);
  }

  selectAllFiles(selected: boolean) {
    this.allFileSelected = selected;
    if (selected) {
      this.selectedFiles = this._files.entities.map(x => x.id);
    } else {
      this.selectedFiles = [];
    }
    this.changeSelectedFiles.emit(this.selectedFiles);
  }

  getSelectedFile(row: FilesModel) {
    const i = this.selectedFiles.findIndex(x => x === row.id);
    return i !== -1;
  }
}
