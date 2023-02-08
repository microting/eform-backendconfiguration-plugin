import {Component, EventEmitter} from '@angular/core';
import {MatDialogRef} from '@angular/material/dialog';

@Component({
  selector: 'app-download-files-name-archive',
  templateUrl: './download-files-name-archive.component.html',
  styleUrls: ['./download-files-name-archive.component.scss']
})
export class DownloadFilesNameArchiveComponent {
  clickDownloadFiles: EventEmitter<string> = new EventEmitter<string>();
  zipName: string = '';

  get disabled() {
    return !this.zipName;
  }

  constructor(public dialogRef: MatDialogRef<DownloadFilesNameArchiveComponent>,) {
  }

  hide() {
    this.dialogRef.close();
  }

  downloadFiles() {
    this.hide();
    this.clickDownloadFiles.emit(this.zipName);
  }
}
