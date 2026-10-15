import {Overlay} from '@angular/cdk/overlay';
import {Component, DestroyRef, HostListener, OnInit, inject} from '@angular/core';
import {takeUntilDestroyed} from '@angular/core/rxjs-interop';
import {MatDialog} from '@angular/material/dialog';
import {Store} from '@ngrx/store';
import {TranslateService} from '@ngx-translate/core';
import {ToastrService} from 'ngx-toastr';
import {EMPTY, Observable, of} from 'rxjs';
import {catchError, finalize, switchMap, tap} from 'rxjs/operators';
import {CommonDictionaryModel} from 'src/app/common/models';
import {BackendConfigurationPnChemicalsService} from '../../../../services';
import {selectChemicalsProperties, selectChemicalsPropertyId} from '../../../../state';
import {ChemicalUnsavedChangesAware} from '../../chemicals-unsaved-changes';
import {
  CHEMICAL_FLAG_KEYS,
  ChemicalFlagKey,
  ChemicalPermissionRow,
  buildPermissionRows,
  changedPermissions,
  confirmDiscard,
  copyPermissionsFrom,
  effectiveFlags,
  isImpliedFlag,
  resetPermissionRows,
  sameFlags,
  toggleFlag,
} from '../../helpers';

/** Label and hint key per flag column (mockup W4 header). */
export const CHEMICAL_FLAG_LABELS: Record<ChemicalFlagKey, {label: string; hint: string}> = {
  view: {label: 'Chemicals: Flag view', hint: 'Chemicals: Flag view hint'},
  register: {label: 'Chemicals: Flag register', hint: 'Chemicals: Flag register hint'},
  remove: {label: 'Chemicals: Flag remove', hint: 'Chemicals: Flag remove hint'},
  stock: {label: 'Chemicals: Flag stock', hint: 'Chemicals: Flag stock hint'},
  manageLocations: {label: 'Chemicals: Flag manage locations', hint: 'Chemicals: Flag manage locations hint'},
  admin: {label: 'Chemicals: Flag admin', hint: 'Chemicals: Flag admin hint'},
};

/**
 * W4 "Medarbejdere & rettigheder": the per-property worker × flag matrix. Edits
 * collect in a save bar and are saved as ONE PUT of only the changed workers
 * (Plan A Task 15). Admin locks the other flags as implied; unticking Admin keeps
 * them ticked (app owner ruling I4). "Kopiér fra anden ejendom" is client-side
 * and lands as unsaved changes (spec §8). Unsaved edits are never dropped
 * silently: leaving the screen, switching property (the container asks through
 * {@link confirmDiscardChanges}) and reloading the tab all ask first. An answer
 * for a property that is no longer selected is ignored.
 *
 * No self-demotion ordering (cf. the app's Plan B B20) and no last-Admin guard:
 * the REST caller is a web admin (ChemicalCaller.Web), which passes every check
 * whatever the worker flags say, and the server applies the whole list in one
 * transaction. A web admin therefore cannot lock themselves or the property out.
 */
@Component({
  selector: 'app-chemical-permissions-page',
  templateUrl: './chemical-permissions-page.component.html',
  styleUrls: ['./chemical-permissions-page.component.scss'],
  standalone: false,
})
export class ChemicalPermissionsPageComponent implements OnInit, ChemicalUnsavedChangesAware {
  private store = inject(Store);
  private chemicalsService = inject(BackendConfigurationPnChemicalsService);
  private destroyRef = inject(DestroyRef);
  private dialog = inject(MatDialog);
  private overlay = inject(Overlay);
  private translate = inject(TranslateService);
  private toastr = inject(ToastrService);

  readonly flagKeys = CHEMICAL_FLAG_KEYS;
  readonly flagLabels = CHEMICAL_FLAG_LABELS;
  /** Null when the customer has no property: the container explains that, the page stays empty. */
  propertyId: number | null = null;
  properties: CommonDictionaryModel[] = [];
  rows: ChemicalPermissionRow[] = [];
  search = '';
  loading = false;
  loadFailed = false;
  saving = false;
  copying = false;
  /** The last save was refused or failed; the edits are kept for a retry. */
  saveFailed = false;
  /** The server's reason for a refused save ('' when the request itself failed). */
  saveError = '';

  ngOnInit(): void {
    this.store.select(selectChemicalsProperties)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((properties) => (this.properties = properties));

    this.store.select(selectChemicalsPropertyId)
      .pipe(
        tap((id) => this.selectProperty(id)),
        // switchMap drops the answer for a property that is no longer selected.
        switchMap((id) => (id === null ? EMPTY : this.chemicalsService.getPermissions(id).pipe(catchError(() => of(null))))),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((res) => {
        this.loading = false;
        if (res && res.success) {
          this.rows = buildPermissionRows(res.model);
        } else {
          this.loadFailed = true;
        }
      });
  }

  /** A save or a copy is in flight: the matrix, Save and Undo are locked so a response cannot race an edit. */
  get busy(): boolean {
    return this.saving || this.copying;
  }

  get otherProperties(): CommonDictionaryModel[] {
    return this.properties.filter((p) => p.id !== this.propertyId);
  }

  get propertyName(): string {
    return this.propertyNameOf(this.propertyId);
  }

  get visibleRows(): ChemicalPermissionRow[] {
    const needle = this.search.trim().toLowerCase();
    return needle ? this.rows.filter((r) => r.workerName.toLowerCase().includes(needle)) : this.rows;
  }

  get changeCount(): number {
    return changedPermissions(this.rows).length;
  }

  /** A toggle replaces the row object; tracking by worker keeps its DOM (and keyboard focus). */
  trackByWorker(_index: number, row: ChemicalPermissionRow): number {
    return row.workerId;
  }

  isChecked(row: ChemicalPermissionRow, key: ChemicalFlagKey): boolean {
    return effectiveFlags(row.current)[key];
  }

  isImplied(row: ChemicalPermissionRow, key: ChemicalFlagKey): boolean {
    return isImpliedFlag(row.current, key);
  }

  isChanged(row: ChemicalPermissionRow, key: ChemicalFlagKey): boolean {
    return effectiveFlags(row.current)[key] !== effectiveFlags(row.stored)[key];
  }

  private t(key: string, params?: object): string {
    return this.translate.instant(key, params);
  }

  /** "Jens Hansen: Lager – Følger af Admin – Ikke gemt": the states the cell shows by style only. */
  checkboxLabel(row: ChemicalPermissionRow, key: ChemicalFlagKey): string {
    const parts = [`${row.workerName}: ${this.t(this.flagLabels[key].label)}`];
    if (this.isImplied(row, key)) {
      parts.push(this.t('Chemicals: Implied by Admin'));
    }
    if (this.isChanged(row, key)) {
      parts.push(this.t('Chemicals: Not saved'));
    }
    return parts.join(' – ');
  }

  toggle(row: ChemicalPermissionRow, key: ChemicalFlagKey): void {
    this.rows = this.rows.map((r) => (r === row ? {...r, current: toggleFlag(r.current, key)} : r));
  }

  undo(): void {
    this.rows = resetPermissionRows(this.rows);
    this.clearSaveError();
  }

  save(): void {
    const propertyId = this.propertyId;
    const changes = changedPermissions(this.rows);
    if (propertyId === null || !changes.length || this.busy) {
      return;
    }
    this.saving = true;
    this.clearSaveError();
    this.chemicalsService.savePermissions(propertyId, changes)
      .pipe(finalize(() => (this.saving = false)))
      .subscribe({
        next: (res) => {
          if (propertyId !== this.propertyId) {
            return;
          }
          if (res && res.success) {
            this.rows = buildPermissionRows(res.model);
          } else {
            // The host toasts the message too; it stays here until the next attempt.
            this.saveFailed = true;
            this.saveError = (res && res.message) || '';
          }
        },
        error: () => {
          if (propertyId === this.propertyId) {
            this.saveFailed = true;
          }
        },
      });
  }

  /** Lands as unsaved changes and says how many workers changed. A refusal is toasted by the host. */
  copyFrom(sourcePropertyId: number): void {
    const propertyId = this.propertyId;
    if (propertyId === null || this.copying) {
      return;
    }
    const property = this.propertyNameOf(sourcePropertyId);
    this.copying = true;
    this.chemicalsService.getPermissions(sourcePropertyId)
      .pipe(finalize(() => (this.copying = false)), takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (res) => {
          if (propertyId !== this.propertyId || !res || !res.success) {
            return;
          }
          const copied = copyPermissionsFrom(this.rows, res.model);
          const count = copied.filter((r, i) => !sameFlags(effectiveFlags(r.current), effectiveFlags(this.rows[i].current))).length;
          this.rows = copied;
          if (count) {
            this.toastr.success(this.t('Chemicals: Copied permissions', {property, count}));
          } else {
            this.toastr.info(this.t('Chemicals: Nothing to copy', {property}));
          }
        },
        error: () => {
          if (propertyId === this.propertyId) {
            this.toastr.error(this.t('Chemicals: Could not copy permissions'));
          }
        },
      });
  }

  /** Used by the route guard and the container's property switch. */
  confirmDiscardChanges(): Observable<boolean> {
    if (this.busy) {
      // A save already issued cannot be cancelled, so "discard" would race its commit: stay until it settles.
      return of(false);
    }
    const count = this.changeCount;
    if (!count) {
      return of(true);
    }
    return confirmDiscard(this.dialog, this.overlay, {
      headerText: this.t('Chemicals: Discard unsaved changes?'),
      fields: [
        {header: this.t('Property'), text: this.propertyName},
        {header: this.t('Chemicals: Changed workers'), text: String(count)},
      ],
      discardText: this.t('Chemicals: Discard changes'),
      keepText: this.t('Chemicals: Keep editing'),
      confirmId: 'chemicalPermissionsDiscardConfirm',
    });
  }

  /** Reload or tab close with unsaved edits: the browser shows its own "leave site?" prompt. */
  @HostListener('window:beforeunload', ['$event'])
  onBeforeUnload(event: BeforeUnloadEvent): void {
    if (this.changeCount > 0) {
      event.preventDefault();
      event.returnValue = '';
    }
  }

  private propertyNameOf(id: number | null): string {
    return this.properties.find((p) => p.id === id)?.name ?? '';
  }

  private selectProperty(id: number | null): void {
    this.propertyId = id;
    this.rows = [];
    this.loadFailed = false;
    this.loading = id !== null;
    this.clearSaveError();
  }

  private clearSaveError(): void {
    this.saveFailed = false;
    this.saveError = '';
  }
}
