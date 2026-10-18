import {Component, DestroyRef, OnInit, inject} from '@angular/core';
import {takeUntilDestroyed} from '@angular/core/rxjs-interop';
import {CdkDragDrop, moveItemInArray} from '@angular/cdk/drag-drop';
import {Overlay} from '@angular/cdk/overlay';
import {MatDialog} from '@angular/material/dialog';
import {TranslateService} from '@ngx-translate/core';
import {Store} from '@ngrx/store';
import {filter, finalize, switchMap} from 'rxjs/operators';
import {dialogConfigHelper} from 'src/app/common/helpers';
import {ChemicalLocationModel} from '../../../../models';
import {BackendConfigurationPnChemicalsService} from '../../../../services';
import {selectChemicalsPropertyId} from '../../../../state';
import {
  ChemicalLatestLoad,
  ChemicalLocationRow,
  activeLocationIds,
  buildLocationRows,
  confirmChemicalAction,
  localToday,
} from '../../helpers';
import {
  ChemicalLocationDialogComponent,
  ChemicalLocationDialogData,
} from '../chemical-location-dialog/chemical-location-dialog.component';

/**
 * W3 "Lokationer": the locations of the selected property with photo, product and action counts.
 * Drag, or the up/down buttons, change the order (the app uses it too); archived locations cannot be
 * moved and are not part of the order. A location can be archived only without open placements
 * (spec §5.1; the server enforces it as well) and only after a confirm, since the screen cannot undo
 * it. "Vis arkiverede" shows the archived ones, read-only.
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
  private translate = inject(TranslateService);
  private destroyRef = inject(DestroyRef);

  propertyId: number | null = null;
  rows: ChemicalLocationRow[] = [];
  showArchived = false;
  /** A new order is being saved; moving waits for it, so two orders never race. */
  reordering = false;
  /** The newest inventory request; nothing is shown as empty while it is in flight. */
  readonly load = new ChemicalLatestLoad();
  /** Bumped by every reload: an order answer from before it carries older locations and is dropped. */
  private rowsGeneration = 0;

  /** Rows keep their elements across a saved order or a reload, so a focused move button survives (N-I1). */
  readonly trackByLocation = (_index: number, row: ChemicalLocationRow): number => row.location.id;

  /** Bound to cdkDropListSortPredicate: a row can only be sorted among the active locations. */
  readonly canSortTo = (index: number): boolean => index < this.activeCount;

  /** An order save cannot be cancelled: the container locks the property picker meanwhile (M3). */
  get busy(): boolean {
    return this.reordering;
  }

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
    this.rowsGeneration++;
    this.load.reset();
    this.rows = [];
    if (this.propertyId !== null) {
      this.load.run(this.chemicalsService.getPropertyInventory(this.propertyId),
        (inventory) => (this.rows = buildLocationRows(inventory, localToday())));
    }
  }

  /** Versioned by the stored file name (a new one per upload), so a replaced photo is never served from a cache. */
  photoUrl(row: ChemicalLocationRow): string | null {
    const fileName = row.location.photoFileName;
    return fileName ? `${this.chemicalsService.locationPhotoUrl(row.location.id)}?v=${encodeURIComponent(fileName)}` : null;
  }

  canArchive(row: ChemicalLocationRow): boolean {
    return !row.location.archived && row.openCount === 0;
  }

  /** The server writes active locations only. */
  canEdit(row: ChemicalLocationRow): boolean {
    return !row.location.archived;
  }

  /** The first active row: its Up button is disabled. */
  isFirstActive(row: ChemicalLocationRow): boolean {
    return this.indexOf(row) === 0;
  }

  /** The last active row: its Down button is disabled. */
  isLastActive(row: ChemicalLocationRow): boolean {
    return this.indexOf(row) === this.activeCount - 1;
  }

  canMoveUp(row: ChemicalLocationRow): boolean {
    return this.canMove(row) && !this.isFirstActive(row);
  }

  canMoveDown(row: ChemicalLocationRow): boolean {
    return this.canMove(row) && !this.isLastActive(row);
  }

  /** The keyboard and single-pointer alternative to dragging (WCAG 2.1.1, 2.5.7). */
  moveUp(row: ChemicalLocationRow): void {
    if (this.canMoveUp(row)) {
      const index = this.indexOf(row);
      this.drop({previousIndex: index, currentIndex: index - 1}, row.location.id);
    }
  }

  moveDown(row: ChemicalLocationRow): void {
    if (this.canMoveDown(row)) {
      const index = this.indexOf(row);
      this.drop({previousIndex: index, currentIndex: index + 1}, row.location.id);
    }
  }

  /**
   * Shows the new order at once. A refused order, or one that gets no answer (the host interceptor
   * ends a 5xx or a network failure with EMPTY), reloads the server's; a saved one takes over the
   * locations the server returns ("Senest ændret" moves), unless the page reloaded meanwhile. A move from a button gives `focusId`: once
   * the save settled and the rows rendered, the moved row's move button gets the focus back (N-I1).
   */
  drop(event: Pick<CdkDragDrop<ChemicalLocationRow[]>, 'previousIndex' | 'currentIndex'>, focusId: number | null = null): void {
    const lastActive = this.activeCount - 1;
    const currentIndex = Math.min(event.currentIndex, lastActive);
    if (this.propertyId === null || this.reordering || event.previousIndex > lastActive || event.previousIndex === currentIndex) {
      return;
    }
    const rows = [...this.rows];
    moveItemInArray(rows, event.previousIndex, currentIndex);
    this.rows = rows;
    this.reordering = true;
    const generation = this.rowsGeneration;
    let answered = false;
    this.chemicalsService.reorderLocations(this.propertyId, activeLocationIds(rows))
      .pipe(finalize(() => {
        this.reordering = false;
        if (!answered) {
          this.reload();
        }
        if (focusId !== null) {
          // After this change detection pass: the buttons are enabled and the rows are in place.
          setTimeout(() => this.focusMoveButton(focusId));
        }
      }))
      .subscribe((res) => {
        answered = true;
        if (generation !== this.rowsGeneration) {
          // The page reloaded meanwhile (an archive or an edit): its rows are newer than this answer.
          return;
        }
        if (res && res.success) {
          this.takeOver(res.model ?? []);
        } else {
          this.reload();
        }
      });
  }

  archive(row: ChemicalLocationRow): void {
    if (!this.canArchive(row)) {
      return;
    }
    const t = (key: string) => this.translate.instant(key);
    confirmChemicalAction(this.dialog, this.overlay, {
      headerText: t('Chemicals: Archive location?'),
      fields: [
        {header: t('Location'), text: row.location.name},
        // The modal shows a header left and a bold value right: the hint is a plain sentence (N-m1).
        {header: t('Chemicals: Archive confirm hint'), text: ''},
      ],
      discardText: t('Chemicals: Archive'),
      keepText: t('Cancel'),
      confirmId: 'chemicalLocationArchiveConfirm',
    }).pipe(
      filter(Boolean),
      switchMap(() => this.chemicalsService.archiveLocation(row.location.id)),
    ).subscribe((res) => {
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

  private canMove(row: ChemicalLocationRow): boolean {
    return !this.reordering && !row.location.archived;
  }

  /** Up, or Down when the row is now first (Up is disabled there); nothing when the row is gone. */
  private focusMoveButton(locationId: number): void {
    const row = this.rows.find((r) => r.location.id === locationId);
    if (!row || row.location.archived) {
      return;
    }
    const button = this.isFirstActive(row) ? 'Down' : 'Up';
    document.getElementById(`chemicalLocation${button}-${locationId}`)?.focus();
  }

  private indexOf(row: ChemicalLocationRow): number {
    return this.rows.findIndex((r) => r.location.id === row.location.id);
  }

  /** The saved locations replace the shown ones; the counts and the order stay. */
  private takeOver(saved: ChemicalLocationModel[]): void {
    const byId = new Map(saved.map((l) => [l.id, l]));
    this.rows = this.rows.map((r) => (byId.has(r.location.id) ? {...r, location: byId.get(r.location.id)} : r));
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
