import {Component, EventEmitter, Input, Output,} from '@angular/core';

@Component({
  selector: 'app-file-create-drop-zone',
  templateUrl: './file-create-drop-zone.component.html',
  styleUrls: ['./file-create-drop-zone.component.scss']
})
export class FileCreateDropZoneComponent {
  @Input() mimePdfType = 'application/pdf';
  @Input() dropZoneHeight = '25vh';
  @Output() filesChanged: EventEmitter<File[]> = new EventEmitter<File[]>();

  constructor() {
  }

  dropHandler(ev) {
    // Prevent default behavior (Prevent file from being opened)
    ev.preventDefault();
    let files;
    if (ev.dataTransfer.files) {
      files = [...ev.dataTransfer.files].filter(x => x.type === this.mimePdfType);
    } else {
      // Use DataTransfer interface to access the file(s)
      files = [...ev.dataTransfer.items].map((x): File => x.getAsFile()).filter(x => x.type === this.mimePdfType);
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
