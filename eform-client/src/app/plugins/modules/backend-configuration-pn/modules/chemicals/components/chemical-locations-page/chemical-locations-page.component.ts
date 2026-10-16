import {Component, DestroyRef, OnInit, inject} from '@angular/core';
import {takeUntilDestroyed} from '@angular/core/rxjs-interop';
import {CdkDragDrop} from '@angular/cdk/drag-drop';
import {Overlay} from '@angular/cdk/overlay';
import {MatDialog} from '@angular/material/dialog';
import {Store} from '@ngrx/store';
import {finalize} from 'rxjs/operators';
import {dialogConfigHelper} from 'src/app/common/helpers';
import {BackendConfigurationPnChemicalsService} from '../../../../services';
import {selectChemicalsPropertyId} from '../../../../state';
import {ChemicalLocationRow, buildLocationRows, localToday, reorderedLocationIds} from '../../helpers';
import {
  ChemicalLocationDialogComponent,
  ChemicalLocationDialogData,
} from '../chemical-location-dialog/chemical-location-dialog.component';

/**
 * W3 "Lokationer": the locations of the selected property with photo, product and action counts.
 * Drag to reorder (the app uses the order too); archived locations cannot be dragged and are not
 * part of the order. A location can be archived only without open placements (spec §5.1; the
 * server enforces it as well). "Vis arkiverede" shows the archived ones, read-only.
 */
@Component({
  selector: 'app-chemical-locations-page',
  templateUrl: './chemical-locations-page.component.html',
  styleUrls: ['./chemical-locations-page.component.scss'],
  standalone: false,
})
export class ChemicalLocationsPageComponent implements OnInit {
  private store = inject(Store);
  private chemicalsService = inject(BackendConfigurationPnChemicalsService);
  private dialog = inject(MatDialog);
  private overlay = inject(Overlay);
  private destroyRef = inject(DestroyRef);

  propertyId: number | null = null;
  rows: ChemicalLocationRow[] = [];
  showArchived = false;
  /** The inventory request is in flight; nothing is shown as empty meanwhile. */
  loading = false;
  loadFailed = false;
  /** A new order is being saved; dragging waits for it, so two orders never race. */
  reordering = false;
  /** Only the newest inventory request may set the rows (another property, or a reload after a save). */
  private loadGeneration = 0;

  /** Bound to cdkDropListSortPredicate: a row can only be sorted among the active locations. */
  readonly canSortTo = (index: number): boolean => index < this.activeCount;

  get visibleRows(): ChemicalLocationRow[] {
    return this.showArchived ? this.rows : this.rows.filter((r) => !r.location.archived);
  }

  get archivedCount(): number {
    return this.rows.filter((r) => r.location.archived).length;
  }

  private get activeCount(): number {
    return this.rows.length - this.archivedCount;
  }

  ngOnInit(): void {
    this.store.select(selectChemicalsPropertyId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((id) => {
        this.propertyId = id;
        this.reload();
      });
  }

  reload(): void {
    const generation = ++this.loadGeneration;
    this.rows = [];
    this.loadFailed = false;
    this.loading = this.propertyId !== null;
    if (this.propertyId === null) {
      return;
    }
    this.chemicalsService.getPropertyInventory(this.propertyId).subscribe({
      next: (res) => {
        if (generation !== this.loadGeneration) {
          return;
        }
        this.loading = false;
        if (!res || !res.success) {
          // ApiBaseService already toasted the server's message.
          this.loadFailed = true;
          return;
        }
        this.rows = buildLocationRows(res.model, localToday());
      },
      error: () => {
        if (generation === this.loadGeneration) {
          this.loading = false;
          this.loadFailed = true;
        }
      },
    });
  }

  photoUrl(row: ChemicalLocationRow): string | null {
    return row.location.photoFileName ? this.chemicalsService.locationPhotoUrl(row.location.id) : null;
  }

  canArchive(row: ChemicalLocationRow): boolean {
    return !row.location.archived && row.openCount === 0;
  }

  /** The server writes active locations only. */
  canEdit(row: ChemicalLocationRow): boolean {
    return !row.location.archived;
  }

  /** Shows the new order at once; a refused order reloads the server's. */
  drop(event: CdkDragDrop<ChemicalLocationRow[]>): void {
    if (this.propertyId === null || this.reordering || event.previousIndex === event.currentIndex) {
      return;
    }
    const ids = reorderedLocationIds(this.rows, event.previousIndex, event.currentIndex);
    const byId = new Map(this.rows.map((r) => [r.location.id, r]));
    this.rows = [...ids.map((id) => byId.get(id)), ...this.rows.filter((r) => r.location.archived)];
    this.reordering = true;
    this.chemicalsService.reorderLocations(this.propertyId, ids)
      .pipe(finalize(() => (this.reordering = false)))
      .subscribe((res) => {
        if (!(res && res.success)) {
          this.reload();
        }
      });
  }

  archive(row: ChemicalLocationRow): void {
    if (!this.canArchive(row)) {
      return;
    }
    this.chemicalsService.archiveLocation(row.location.id).subscribe((res) => {
      if (res && res.success) {
        this.reload();
      }
    });
  }

  openCreate(): void {
    this.openDialog(null);
  }

  openEdit(row: ChemicalLocationRow): void {
    if (this.canEdit(row)) {
      this.openDialog(row);
    }
  }

  private openDialog(row: ChemicalLocationRow | null): void {
    if (this.propertyId === null) {
      return;
    }
    const data: ChemicalLocationDialogData = {propertyId: this.propertyId, row, photoUrl: row ? this.photoUrl(row) : null};
    this.dialog.open<ChemicalLocationDialogComponent, ChemicalLocationDialogData, boolean>(
      ChemicalLocationDialogComponent, dialogConfigHelper(this.overlay, data),
    ).afterClosed().subscribe((saved) => {
      if (saved) {
        this.reload();
      }
    });
  }
}
