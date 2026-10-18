import {Component, EventEmitter,
  inject
} from '@angular/core';
import {MatDialogRef, MatDialogTitle, MatDialogContent, MatDialogActions} from '@angular/material/dialog';
import {FormsModule} from '@angular/forms';
import {TranslatePipe} from '@ngx-translate/core';
import {MatFormField, MatLabel} from '@angular/material/form-field';
import {MatInput} from '@angular/material/input';

@Component({
    selector: 'app-download-files-name-archive',
    imports: [FormsModule, TranslatePipe, MatFormField, MatLabel, MatInput, MatDialogTitle, MatDialogContent, MatDialogActions],
    templateUrl: './download-files-name-archive.component.html',
    styleUrls: ['./download-files-name-archive.component.scss'],
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
