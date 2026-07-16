import {Component, Inject} from '@angular/core';
import {MAT_DIALOG_DATA, MatDialogRef} from '@angular/material/dialog';

export interface CalendarImageLightboxData {
  /** SDK uploaded-data file names, rendered via template-files/get-image. */
  images: string[];
  startIndex: number;
}

/**
 * Centered dark lightbox for the historical task card's case images.
 * ‹ › wrap around; hidden with a single image. Esc + backdrop click close
 * (MatDialog defaults — the opener passes disableClose: false).
 */
@Component({
  standalone: false,
  selector: 'app-calendar-image-lightbox',
  templateUrl: './calendar-image-lightbox.component.html',
  styleUrls: ['./calendar-image-lightbox.component.scss'],
})
export class CalendarImageLightboxComponent {
  currentIndex: number;

  constructor(
    private dialogRef: MatDialogRef<CalendarImageLightboxComponent>,
    @Inject(MAT_DIALOG_DATA) public data: CalendarImageLightboxData,
  ) {
    const count = data?.images?.length ?? 0;
    this.currentIndex = count > 0
      ? Math.min(Math.max(data.startIndex ?? 0, 0), count - 1)
      : 0;
  }

  get currentSrc(): string {
    return `/api/template-files/get-image/${this.data.images[this.currentIndex]}`;
  }

  prev() {
    const count = this.data.images.length;
    this.currentIndex = (this.currentIndex - 1 + count) % count;
  }

  next() {
    this.currentIndex = (this.currentIndex + 1) % this.data.images.length;
  }

  close() {
    this.dialogRef.close();
  }
}
