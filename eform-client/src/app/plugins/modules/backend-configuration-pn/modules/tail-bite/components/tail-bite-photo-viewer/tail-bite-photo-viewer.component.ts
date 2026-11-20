import {DatePipe, NgIf} from '@angular/common';
import {Component, OnDestroy, OnInit, inject} from '@angular/core';
import {MatButtonModule} from '@angular/material/button';
import {MAT_DIALOG_DATA, MatDialogModule} from '@angular/material/dialog';
import {MatIconModule} from '@angular/material/icon';
import {MatTooltipModule} from '@angular/material/tooltip';
import {TranslateModule} from '@ngx-translate/core';
import {Subscription} from 'rxjs';
import {BackendConfigurationPnTailBiteService} from '../../../../services';
import {TailBitePhotoBlobs} from '../../shared/tail-bite-photo-blobs';
import {TailBiteGalleryItem} from '../tail-bite-outbreak-detail/tail-bite-outbreak-view';

/**
 * `items` in the order previous/next walks them, `index` the one to open on. `blobs` shares the page's fetched photos;
 * without it the viewer fetches its own.
 */
export interface TailBitePhotoViewerData {
  items: TailBiteGalleryItem[];
  index: number;
  blobs?: TailBitePhotoBlobs;
}

/**
 * One photo at a time, full size, with previous/next (wrapping around), its caption and a download link. The object URL
 * of the shown photo is this dialog's own: revoked when it moves to another photo and when it closes.
 */
@Component({
  selector: 'app-tail-bite-photo-viewer',
  templateUrl: './tail-bite-photo-viewer.component.html',
  imports: [DatePipe, NgIf, MatButtonModule, MatDialogModule, MatIconModule, MatTooltipModule, TranslateModule],
  styles: [`
    .tail-bite-photo-viewer-stage {
      display: flex;
      align-items: center;
      justify-content: center;
      min-height: 320px;
    }
    .tail-bite-photo-viewer-img {
      display: block;
      max-width: 100%;
      max-height: 70vh;
      object-fit: contain;
    }
    .tail-bite-photo-viewer-failed {
      display: flex;
      flex-direction: column;
      align-items: center;
      gap: var(--spacing-sm);
      color: var(--md-on-surface-variant);
    }
  `],
})
export class TailBitePhotoViewerComponent implements OnInit, OnDestroy {
  readonly data = inject<TailBitePhotoViewerData>(MAT_DIALOG_DATA);
  private service = inject(BackendConfigurationPnTailBiteService);
  private blobs = this.data.blobs ?? new TailBitePhotoBlobs((uuid) => this.service.getPhotoBlob(uuid));

  index = 0;
  url: string | null = null;
  failed = false;
  /** The download's file name, with the extension of the shown photo's type. */
  fileName = '';
  private sub?: Subscription;

  get item(): TailBiteGalleryItem | undefined {
    return this.data.items[this.index];
  }

  ngOnInit(): void {
    this.show(this.data.index);
  }

  ngOnDestroy(): void {
    this.release();
  }

  previous(): void {
    this.show(this.index - 1);
  }

  next(): void {
    this.show(this.index + 1);
  }

  /** The bytes arrived but are not an image the browser can show. */
  onImageError(): void {
    this.revoke();
    this.failed = true;
  }

  private show(index: number): void {
    const count = this.data.items.length;
    if (count === 0) {
      return;
    }
    this.index = ((index % count) + count) % count;
    this.release();
    this.failed = false;
    const uuid = this.data.items[this.index].uuid;
    this.sub = this.blobs.get(uuid).subscribe({
      next: (blob) => {
        this.revoke();
        this.url = URL.createObjectURL(blob);
        this.fileName = `halebid-${uuid}.${blob.type === 'image/png' ? 'png' : 'jpg'}`;
      },
      error: () => (this.failed = true),
      // The host's error interceptor answers a 404 by completing without a value.
      complete: () => (this.failed = this.url === null),
    });
  }

  private release(): void {
    this.sub?.unsubscribe();
    this.revoke();
  }

  private revoke(): void {
    if (this.url) {
      URL.revokeObjectURL(this.url);
      this.url = null;
    }
  }
}
