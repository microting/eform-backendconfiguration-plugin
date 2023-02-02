import {Component, EventEmitter, Input, OnChanges, OnDestroy, Output, SimpleChanges,} from '@angular/core';
import {SharedTagModel} from 'src/app/common/models';
import {PDFDocument, PDFPage} from 'pdf-lib';
import {FilesCreateModel} from '../../../../../../models';
import * as R from 'ramda';
import {DragulaService} from 'ng2-dragula';

@Component({
  selector: 'app-file-create-edit-file',
  templateUrl: './file-create-edit-file.component.html',
  styleUrls: ['./file-create-edit-file.component.scss']
})
export class FileCreateEditFileComponent implements OnChanges, OnDestroy {
  @Output() saveFile: EventEmitter<FilesCreateModel> = new EventEmitter<FilesCreateModel>();
  @Output() cancelSaveFile: EventEmitter<void> = new EventEmitter<void>();
  @Input() file: FilesCreateModel;
  @Input() availableProperties: { name: string; id: number }[];
  selectedTags: number[] = [];
  selectedProperty: number;
  pagesInFile: number[] = [];
  changedPagesInFile: number[] = [];
  fileAsPdfDocument: PDFDocument;

  get pageInFile(): number[] {
    if (this.fileAsPdfDocument) {
      return R.range(0, this.fileAsPdfDocument.getPages().length);
    }
    return [];
  }


  @Input()
  get availableTags(): SharedTagModel[] {
    return this._availableTags;
  }

  set availableTags(val: SharedTagModel[]) {
    this._availableTags = val ?? [];
    if (this.selectedTags) {
      // delete from selector deleted tags
      const newTagIdsWithoutDeletedTags = this.selectedTags.filter((x: number) => this._availableTags.some(y => y.id === x));
      if (newTagIdsWithoutDeletedTags.length !== this.selectedTags.length) {
        this.selectedTags = newTagIdsWithoutDeletedTags;
      }
    }
  }

  private _availableTags: SharedTagModel[] = [];

  constructor(private dragulaService: DragulaService) {
    this.dragulaService.createGroup('pageContainer', {
      moves: (el, container, handle) => {
        return handle.classList.contains('dragula-handle');
      },
      accepts: (el, target) => {
        return target.id.includes(`dragula-container`);
      },
    });
  }

  deletePage(indexPage: number) {
    this.fileAsPdfDocument.removePage(indexPage);
    this.fileAsPdfDocument.save().then(async x => {
      this.file.file = new File([x], this.file.file.name, {type: this.file.file.type});
      this.file.src = x;
      this.fileAsPdfDocument = await PDFDocument.load(x);
      this.pagesInFile = R.range(0, this.fileAsPdfDocument.getPages().length);
      this.changedPagesInFile = R.range(0, this.fileAsPdfDocument.getPages().length);
    });
  }

  async ngOnChanges(changes: SimpleChanges) {
    if (changes && changes.file && changes.file.currentValue) {
      ({...this.file}).file.arrayBuffer().then(arrayBuffer => {
        PDFDocument.load(arrayBuffer).then(pdf => {
          pdf.copy().then(x => {
            this.fileAsPdfDocument = x;
            this.pagesInFile = R.range(0, this.fileAsPdfDocument.getPages().length);
            this.changedPagesInFile = R.range(0, this.fileAsPdfDocument.getPages().length);
          });
          this.selectedTags = [...this.file.tagIds];
          this.selectedProperty = this.file.propertyId;
        });
      });
    }
  }

  saveEditFile() {
    // change position pages
    const sortByNewPosition = R.sortBy(R.prop('newPosition'));
    let differentPages: { newPosition: number, page: PDFPage }[] = this.changedPagesInFile.map(x => ({newPosition: x, page: undefined}));
    this.fileAsPdfDocument.copyPages(this.fileAsPdfDocument, this.changedPagesInFile).then(copiedPages => {
      copiedPages.forEach((x, i) => {
        const index = differentPages.findIndex(y => y.newPosition === i);
        if (index !== -1) {
          differentPages[index].page = x;
        }
      });
      this.fileAsPdfDocument.getPages().map((_, i) => i).sort().reverse().forEach(i => this.fileAsPdfDocument.removePage(i));
      sortByNewPosition(differentPages).forEach(x => this.fileAsPdfDocument.insertPage(x.newPosition, x.page));
      this.fileAsPdfDocument.save().then(async x => {
        this.file.file = new File([x], this.file.file.name, {type: this.file.file.type});
        this.file.src = x;
        this.file.tagIds = this.selectedTags;
        this.file.propertyId = this.selectedProperty;
        this.saveFile.emit(this.file);
      });
    });
  }

  cancelEditFile() {
    this.cancelSaveFile.emit();
  }

  ngOnDestroy(): void {
    this.dragulaService.destroy('pageContainer');
  }
}
