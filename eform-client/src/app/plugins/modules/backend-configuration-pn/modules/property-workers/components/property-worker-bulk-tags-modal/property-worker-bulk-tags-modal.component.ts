import {Component, inject} from '@angular/core';
import {MAT_DIALOG_DATA, MatDialogRef} from '@angular/material/dialog';
import {CommonDictionaryModel} from 'src/app/common/models';
import {DeviceUserModel, WorkerTagsBulkMode} from '../../../../models';
import {BackendConfigurationPnPropertiesService} from '../../../../services';

export interface PropertyWorkerBulkTagsModalData {
  workers: DeviceUserModel[];
  availableTags: CommonDictionaryModel[];
}

/**
 * #1380 — "Tildel tags": adds or removes the chosen tags on every selected
 * worker. Add/Remove only, no replace, so a worker's other tags are never
 * wiped by accident. Closes with `true` after a successful save.
 */
@Component({
  selector: 'app-property-worker-bulk-tags-modal',
  templateUrl: './property-worker-bulk-tags-modal.component.html',
  standalone: false,
})
export class PropertyWorkerBulkTagsModalComponent {
  private propertiesService = inject(BackendConfigurationPnPropertiesService);
  public dialogRef = inject(MatDialogRef<PropertyWorkerBulkTagsModalComponent>);
  public data = inject<PropertyWorkerBulkTagsModalData>(MAT_DIALOG_DATA);

  readonly modes = WorkerTagsBulkMode;
  mode = WorkerTagsBulkMode.Add;
  tagIds: number[] = [];
  saving = false;

  get valid(): boolean {
    return this.tagIds.length > 0 && this.data.workers.length > 0 && !this.saving;
  }

  hide(result = false) {
    this.dialogRef.close(result);
  }

  save() {
    if (!this.valid) {
      return;
    }
    this.saving = true;
    this.propertiesService
      .bulkUpdateWorkerTags({
        siteIds: this.data.workers.map((w) => w.siteId),
        tagIds: this.tagIds,
        mode: this.mode,
      })
      .subscribe({
        next: (res) => {
          this.saving = false;
          if (res?.success) {
            this.hide(true);
          }
        },
        error: () => (this.saving = false),
      });
  }
}
