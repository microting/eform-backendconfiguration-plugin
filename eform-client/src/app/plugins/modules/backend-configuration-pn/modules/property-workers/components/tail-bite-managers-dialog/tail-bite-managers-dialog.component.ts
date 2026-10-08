import {NgFor, NgIf} from '@angular/common';
import {Component, OnInit, inject} from '@angular/core';
import {FormsModule} from '@angular/forms';
import {MAT_DIALOG_DATA, MatDialogModule, MatDialogRef} from '@angular/material/dialog';
import {MatFormFieldModule} from '@angular/material/form-field';
import {MatSlideToggleModule} from '@angular/material/slide-toggle';
import {MtxSelectModule} from '@ng-matero/extensions/select';
import {TranslateModule} from '@ngx-translate/core';
import {forkJoin} from 'rxjs';
import {finalize} from 'rxjs/operators';
import {TailBitePropertyStatus, TailBiteWorker} from '../../../../models';
import {BackendConfigurationPnTailBiteService} from '../../../../services';

export interface TailBiteManagersDialogData {
  /** Preselected property: the property-workers filter when exactly one property is chosen. */
  propertyId: number | null;
}

/**
 * "Halebid-ansvarlige": enable tail biting on a property and choose its managers (spec §5,
 * PropertyWorker.TailBiteManager). Several managers per property are allowed. Plugin-admin routes:
 * no worker identity is needed to use this dialog.
 */
@Component({
  selector: 'app-tail-bite-managers-dialog',
  templateUrl: './tail-bite-managers-dialog.component.html',
  imports: [NgFor, NgIf, FormsModule, MatDialogModule, MatFormFieldModule, MatSlideToggleModule, MtxSelectModule, TranslateModule],
})
export class TailBiteManagersDialogComponent implements OnInit {
  private service = inject(BackendConfigurationPnTailBiteService);
  private dialogRef = inject(MatDialogRef<TailBiteManagersDialogComponent>);
  public data = inject<TailBiteManagersDialogData>(MAT_DIALOG_DATA);

  properties: TailBitePropertyStatus[] = [];
  propertyId: number | null = null;
  workers: TailBiteWorker[] = [];
  busy = false;

  ngOnInit(): void {
    this.loadProperties(this.data?.propertyId ?? null);
  }

  get selectedProperty(): TailBitePropertyStatus | null {
    return this.properties.find((p) => p.propertyId === this.propertyId) ?? null;
  }

  onPropertyChange(propertyId: number | null): void {
    this.propertyId = propertyId;
    this.loadWorkers();
  }

  enable(): void {
    const id = this.propertyId;
    if (id === null || this.busy) {
      return;
    }
    this.busy = true;
    this.service
      .enable(id)
      .pipe(finalize(() => (this.busy = false)))
      .subscribe({
        next: (res) => {
          if (res?.success) {
            this.loadProperties(id);
          }
        },
        // A failed enable may still have gone through on the server: show the truth.
        error: () => this.loadProperties(id),
      });
  }

  /** Sets every PropertyWorker row of the worker on the property, then reloads the truth from the server. */
  setManager(worker: TailBiteWorker, isManager: boolean): void {
    if (this.busy || worker.propertyWorkerIds.length === 0) {
      return;
    }
    this.busy = true;
    // Success or failure, reload the truth from the server so a refused toggle flips back.
    forkJoin(worker.propertyWorkerIds.map((id) => this.service.setManager(id, isManager)))
      .pipe(
        finalize(() => {
          this.busy = false;
          this.loadWorkers();
        }),
      )
      .subscribe({error: () => undefined});
  }

  close(): void {
    this.dialogRef.close();
  }

  private loadProperties(preselect: number | null): void {
    this.service.getProperties().subscribe({
      next: (res) => {
        this.properties = res?.success ? res.model : [];
        const selected = this.properties.find((p) => p.propertyId === preselect) ?? this.properties[0];
        this.onPropertyChange(selected?.propertyId ?? null);
      },
      error: () => {
        this.properties = [];
        this.onPropertyChange(null);
      },
    });
  }

  private loadWorkers(): void {
    const id = this.propertyId;
    // The previous property's workers must not stay toggleable while the new list loads, or if it fails.
    this.workers = [];
    if (id === null) {
      return;
    }
    this.service.getWorkers(id).subscribe({
      next: (res) => {
        if (id === this.propertyId) {
          this.workers = res?.success ? res.model : [];
        }
      },
      error: () => undefined,
    });
  }
}
