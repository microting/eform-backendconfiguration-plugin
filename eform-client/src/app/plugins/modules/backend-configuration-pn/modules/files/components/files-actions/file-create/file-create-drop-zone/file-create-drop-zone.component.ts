import {Component, EventEmitter, Input, Output,} from '@angular/core';
import {TranslatePipe} from '@ngx-translate/core';
import {ARCHIVE_UPLOAD_MIME_TYPES} from '../../../../archive-file-kind';

@Component({
    selector: 'app-file-create-drop-zone',
    imports: [TranslatePipe],
    templateUrl: './file-create-drop-zone.component.html',
    styleUrls: ['./file-create-drop-zone.component.scss'],
})
export class FileCreateDropZoneComponent {
  /** MIME types a dropped file may have; the picker's `accept` is the same list. */
  @Input() acceptedMimeTypes: readonly string[] = ARCHIVE_UPLOAD_MIME_TYPES;
  @Input() dropZoneHeight = '25vh';
  @Output() filesChanged: EventEmitter<File[]> = new EventEmitter<File[]>();

  get accept(): string {
    return this.acceptedMimeTypes.join(',');
  }

  private isAccepted = (file: File): boolean => this.acceptedMimeTypes.includes(file.type);

  dropHandler(ev) {
    // Prevent default behavior (Prevent file from being opened)
    ev.preventDefault();
    let files;
    if (ev.dataTransfer.files) {
      files = [...ev.dataTransfer.files].filter(this.isAccepted);
    } else {
      // Use DataTransfer interface to access the file(s)
      files = [...ev.dataTransfer.items].map((x): File => x.getAsFile()).filter(this.isAccepted);
    }
    this.filesChanged.emit(files);
  }

  dragOverHandler(ev: DragEvent) {
    // Prevent default behavior (Prevent file from being opened)
    ev.preventDefault();
  }

  onFileSelected(event: Event) {
    // @ts-ignore
    this.filesChanged.emit([...event.target.files]);
  }
}
