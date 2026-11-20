import {NgIf} from '@angular/common';
import {Component, Input, OnChanges, OnDestroy} from '@angular/core';
import {MatIconModule} from '@angular/material/icon';
import {MatTooltipModule} from '@angular/material/tooltip';
import {TranslateModule} from '@ngx-translate/core';
import {Subscription} from 'rxjs';
import {TailBitePhotoBlobs} from '../../shared/tail-bite-photo-blobs';

/**
 * One tail-bite photo as a cropped tile, filling the box its parent gives it. The object URL is this component's own:
 * made from the Blob, revoked when the photo changes and on destroy. A photo that cannot be fetched or decoded shows
 * a placeholder tile; nothing is toasted (one toast per tile would flood the page).
 */
@Component({
  selector: 'app-tail-bite-photo-thumb',
  imports: [NgIf, MatIconModule, MatTooltipModule, TranslateModule],
  template: `
    <img *ngIf="url" class="tail-bite-photo-thumb-img" [src]="url" [alt]="alt" (error)="onImageError()" />
    <span *ngIf="failed" class="tail-bite-photo-thumb-failed" [matTooltip]="'The photo could not be loaded' | translate">
      <mat-icon>broken_image</mat-icon>
    </span>
  `,
  styles: [`
    :host {
      display: block;
      width: 100%;
      height: 100%;
      overflow: hidden;
      border-radius: var(--radius-sm);
      background: var(--md-surface-variant);
    }
    .tail-bite-photo-thumb-img {
      display: block;
      width: 100%;
      height: 100%;
      object-fit: cover;
    }
    .tail-bite-photo-thumb-failed {
      display: flex;
      align-items: center;
      justify-content: center;
      width: 100%;
      height: 100%;
      color: var(--md-on-surface-variant);
    }
  `],
})
export class TailBitePhotoThumbComponent implements OnChanges, OnDestroy {
  @Input({required: true}) photoUuid!: string;
  @Input({required: true}) blobs!: TailBitePhotoBlobs;
  /** Already translated. */
  @Input() alt = '';

  url: string | null = null;
  failed = false;
  private sub?: Subscription;

  ngOnChanges(): void {
    this.release();
    this.failed = false;
    this.sub = this.blobs.get(this.photoUuid).subscribe({
      next: (blob) => {
        this.revoke();
        this.url = URL.createObjectURL(blob);
      },
      error: () => (this.failed = true),
      // The host's error interceptor answers a 404 by completing without a value.
      complete: () => (this.failed = this.url === null),
    });
  }

  ngOnDestroy(): void {
    this.release();
  }

  /** The bytes arrived but are not an image the browser can show. */
  onImageError(): void {
    this.revoke();
    this.failed = true;
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
