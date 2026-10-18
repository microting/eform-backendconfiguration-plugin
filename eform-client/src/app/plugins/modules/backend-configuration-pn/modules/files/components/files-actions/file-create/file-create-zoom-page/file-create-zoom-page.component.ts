import {Component,
  inject
} from '@angular/core';
import {MAT_DIALOG_DATA, MatDialogRef, MatDialogContent} from '@angular/material/dialog';
import {PdfViewerModule} from 'ng2-pdf-viewer';

@Component({
    selector: 'app-file-create-zoom-page',
    imports: [MatDialogContent, PdfViewerModule],
    templateUrl: './file-create-zoom-page.component.html',
    styleUrls: ['./file-create-zoom-page.component.scss'],
})
export class FileCreateZoomPageComponent {
  public dialogRef = inject(MatDialogRef<FileCreateZoomPageComponent>);
  private model = inject<{ page: number, src: Uint8Array }>(MAT_DIALOG_DATA);

  page: number = 0;
  src: Uint8Array = new Uint8Array();
  zoom: number = 1;

  
  constructor() {
    this.page = this.model.page;
    this.src = this.model.src;
  }

}
