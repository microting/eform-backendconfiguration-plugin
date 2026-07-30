import {Component, Inject, OnInit} from '@angular/core';
import {MAT_DIALOG_DATA, MatDialogRef} from '@angular/material/dialog';
import {Subscription} from 'rxjs';
import {AutoUnsubscribe} from 'ngx-auto-unsubscribe';
import {AdhocAreaModel} from '../../../../models';
import {AdhocStateService} from '../store';

export interface AdhocAreaAdminModalData {
  propertyId: number;
  propertyName: string;
}

/**
 * "Administrer områder" modal (mockup #omraade-admin-modal + inline
 * delete confirm). Lists ALL active areas of the property (deliberate
 * mockup deviation - `AdhocArea` has no creator field; in production every
 * area is user-created). Delete is soft (spec decision 2): the confirm copy
 * says tasks KEEP their history - do not "fix" it to the mockup's
 * "cleared" wording. Closes with `true` if anything changed.
 */
@AutoUnsubscribe()
@Component({
  selector: 'app-adhoc-area-admin-modal',
  templateUrl: './adhoc-area-admin-modal.component.html',
  styleUrls: ['./adhoc-area-admin-modal.component.scss'],
  standalone: false,
})
export class AdhocAreaAdminModalComponent implements OnInit {
  areas: AdhocAreaModel[] = [];
  editingId: number | null = null;
  editName = '';
  confirmDeleteArea: AdhocAreaModel | null = null;
  busy = false;
  changed = false;
  errorKey: string | null = null;
  loadSub$: Subscription;
  renameSub$: Subscription;
  deleteSub$: Subscription;

  constructor(
    public dialogRef: MatDialogRef<AdhocAreaAdminModalComponent>,
    @Inject(MAT_DIALOG_DATA) public data: AdhocAreaAdminModalData,
    private adhocStateService: AdhocStateService,
  ) {
  }

  ngOnInit(): void {
    this.reload();
  }

  private reload(): void {
    this.loadSub$ = this.adhocStateService
      .getAreasForProperty(this.data.propertyId)
      .subscribe((areas) => (this.areas = areas));
  }

  startRename(area: AdhocAreaModel): void {
    this.editingId = area.id;
    this.editName = area.name;
    this.errorKey = null;
  }

  saveRename(area: AdhocAreaModel): void {
    const name = this.editName.trim();
    if (name.length === 0 || this.busy) {
      return;
    }
    this.busy = true;
    this.renameSub$ = this.adhocStateService
      .renameArea(this.data.propertyId, area.id, name)
      .subscribe((success) => {
        this.busy = false;
        if (success) {
          this.changed = true;
          this.editingId = null;
          this.errorKey = null;
          this.areas = this.adhocStateService.getCachedAreas(this.data.propertyId);
        } else {
          this.errorKey = 'Area name is empty or already exists';
        }
      });
  }

  askDelete(area: AdhocAreaModel): void {
    this.confirmDeleteArea = area;
  }

  cancelDelete(): void {
    this.confirmDeleteArea = null;
  }

  confirmDelete(): void {
    const area = this.confirmDeleteArea;
    if (!area || this.busy) {
      return;
    }
    this.busy = true;
    this.deleteSub$ = this.adhocStateService
      .deleteArea(this.data.propertyId, area.id)
      .subscribe((success) => {
        this.busy = false;
        this.confirmDeleteArea = null;
        if (success) {
          this.changed = true;
          this.areas = this.adhocStateService.getCachedAreas(this.data.propertyId);
        }
      });
  }

  close(): void {
    this.dialogRef.close(this.changed);
  }
}
