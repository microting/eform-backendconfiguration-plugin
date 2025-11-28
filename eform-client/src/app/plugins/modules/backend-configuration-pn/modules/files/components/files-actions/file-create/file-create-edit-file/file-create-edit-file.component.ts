import {Component, EventEmitter, Input, OnChanges, OnDestroy, Output, SimpleChanges, inject} from '@angular/core';
import {dialogConfigHelper} from 'src/app/common/helpers';
import {FilesCreateModel} from '../../../../../../models';
import {AutoUnsubscribe} from 'ngx-auto-unsubscribe';
import {SharedTagModel} from 'src/app/common/models';
import {MatDialog} from '@angular/material/dialog';
import {FileCreateZoomPageComponent} from '../../';
import {PDFDocument, PDFPage} from 'pdf-lib';
import {Overlay} from '@angular/cdk/overlay';
import {DragulaService} from 'ng2-dragula';
import {Subscription} from 'rxjs';
import * as R from 'ramda';

@AutoUnsubscribe()
@Component({
    selector: 'app-file-create-edit-file',
    templateUrl: './file-create-edit-file.component.html',
    styleUrls: ['./file-create-edit-file.component.scss'],
    standalone: false
})
export class FileCreateEditFileComponent implements OnChanges, OnDestroy {
  private dragulaService = inject(DragulaService);
  public dialog = inject(MatDialog);
  private overlay = inject(Overlay);

  @Output() saveFile: EventEmitter<FilesCreateModel> = new EventEmitter<FilesCreateModel>();
  @Output() cancelSaveFile: EventEmitter<void> = new EventEmitter<void>();
  @Input() file: FilesCreateModel;
  @Input() availableProperties: { name: string; id: number }[];
  private _availableTags: SharedTagModel[] = [];
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
  selectedTags: number[] = [];
  selectedProperties: number[] = [];
  pagesInFile: number[] = [];
  changedPagesInFile: number[] = [];
  fileAsPdfDocument: PDFDocument;
  dragulaContainerName = 'pages';
  dragulaContainerId = 'dragula-container';
  dragulaHandle = 'dragula-handle';
  progressLoad: number = 0;

  zoomPageModalBackdropClickSub$: Subscription;

  get pageInFile(): number[] {
    //if (this.fileAsPdfDocument) {
      return R.range(0, this.fileAsPdfDocument.getPages().length);
    //}
    //return [];
  }

  
  constructor() {
    this.dragulaService.createGroup(this.dragulaContainerName, {
      moves: (el, container, handle) => {
        return handle.classList.contains(this.dragulaHandle);
      },
      accepts: (el, target) => {
        return target.id.includes(this.dragulaContainerId);
      },
      direction:'horizontal'
    });
  }


  deletePage(indexPage: number) {
    this.fileAsPdfDocument.removePage(indexPage);
    this.fileAsPdfDocument.save().then(async x => {
      this.file.file = new File([x], this.file.file.name, {type: this.file.file.type});
      this.file.src = x;
      this.fileAsPdfDocument = await PDFDocument.load(x);
      this.pagesInFile = R.range(0, this.fileAsPdfDocument.getPages().length);
      this.changedPagesInFile = this.changedPagesInFile.filter(x => x !== indexPage);
    });
  }

  async ngOnChanges(changes: SimpleChanges) {
    if (changes && changes.file && changes.file.currentValue) {
      this.file.file.arrayBuffer().then(arrayBuffer => {
        this.progressLoad = 50;
        PDFDocument.load(arrayBuffer).then(pdf => {
          this.fileAsPdfDocument = pdf;
          this.changedPagesInFile = this.pagesInFile = R.range(0, this.fileAsPdfDocument.getPages().length);
          this.selectedTags = [...this.file.tagIds];
          this.selectedProperties = this.file.propertyIds;
          this.progressLoad = 100;
        });
      });
    }
  }

  saveEditFile() {
    // change position pages
    let differentPages: { newPosition: number, page: PDFPage }[] = this.changedPagesInFile.map(x => ({newPosition: x, page: undefined}));
    // copy pages for sort and insert to file
    this.fileAsPdfDocument.copyPages(this.fileAsPdfDocument, this.changedPagesInFile).then(copiedPages => {
      copiedPages.forEach((x, i) => {
        const index = differentPages.findIndex(y => y.newPosition === i);
        if (index !== -1) {
          differentPages[index].page = x;
        }
      });
      // remove all pages from last page to start page
      R.sortBy(x => x, this.fileAsPdfDocument.getPages().map((_, i) => i)).reverse()
        .forEach(i => this.fileAsPdfDocument.removePage(i));
      // insert pages in the new order
      R.sortBy(x => x.newPosition, differentPages).forEach(x => this.fileAsPdfDocument.insertPage(x.newPosition, x.page));
      // save changes in file
      this.fileAsPdfDocument.save().then(x => {
        this.file.file = new File([x], this.file.file.name, {type: this.file.file.type});
        this.file.src = x;
        this.file.tagIds = this.selectedTags;
        this.file.propertyIds = this.selectedProperties;
        this.saveFile.emit(this.file);
      });
    });
  }

  cancelEditFile() {
    // this.fileAsPdfDocument.context.
    this.cancelSaveFile.emit();
  }

  zoomPage(page: number) {
    const zoomPageModal = this.dialog.open(FileCreateZoomPageComponent,
      {...dialogConfigHelper(this.overlay, {page: page + 1, src: this.file.src})}); // for viewer 0 and 1 - it's first page, so need page + 1
    this.zoomPageModalBackdropClickSub$ = zoomPageModal.backdropClick().subscribe(() => {
      zoomPageModal.close();
    });
  }

  ngOnDestroy(): void {
    this.dragulaService.destroy(this.dragulaContainerName);
  }
}
