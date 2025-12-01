import {Component, EventEmitter,
  inject
} from '@angular/core';
import {MatDialogRef} from '@angular/material/dialog';

@Component({
    selector: 'app-download-files-name-archive',
    templateUrl: './download-files-name-archive.component.html',
    styleUrls: ['./download-files-name-archive.component.scss'],
    standalone: false
})
export class DownloadFilesNameArchiveComponent {
  public dialogRef = inject(MatDialogRef<DownloadFilesNameArchiveComponent>);

  clickDownloadFiles: EventEmitter<string> = new EventEmitter<string>();
  zipName: string = '';

  get disabled() {
    return !this.zipName;
  }

  

  hide() {
    this.dialogRef.close();
  }

  downloadFiles() {
    this.hide();
    this.clickDownloadFiles.emit(this.zipName);
  }
}
