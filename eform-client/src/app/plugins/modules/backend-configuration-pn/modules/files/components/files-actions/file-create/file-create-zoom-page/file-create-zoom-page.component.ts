import {Component, Inject} from '@angular/core';
import {MAT_DIALOG_DATA, MatDialogRef} from '@angular/material/dialog';

@Component({
  selector: 'app-file-create-zoom-page',
  templateUrl: './file-create-zoom-page.component.html',
  styleUrls: ['./file-create-zoom-page.component.scss']
})
export class FileCreateZoomPageComponent {
  page: number = 0;
  src: Uint8Array = new Uint8Array();
  zoom: number = 1;

  constructor(
    public dialogRef: MatDialogRef<FileCreateZoomPageComponent>,
    @Inject(MAT_DIALOG_DATA) model: { page: number, src: Uint8Array },
  ) {
    this.page = model.page;
    this.src = model.src;
  }
}
