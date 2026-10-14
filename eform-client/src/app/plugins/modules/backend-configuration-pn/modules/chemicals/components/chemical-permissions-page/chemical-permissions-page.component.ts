import {Component, DestroyRef, OnInit, inject} from '@angular/core';
import {takeUntilDestroyed} from '@angular/core/rxjs-interop';
import {Store} from '@ngrx/store';
import {EMPTY, of} from 'rxjs';
import {catchError, finalize, switchMap, tap} from 'rxjs/operators';
import {CommonDictionaryModel} from 'src/app/common/models';
import {BackendConfigurationPnChemicalsService, BackendConfigurationPnPropertiesService} from '../../../../services';
import {selectChemicalsPropertyId} from '../../../../state';
import {
  CHEMICAL_FLAG_KEYS,
  ChemicalFlagKey,
  ChemicalPermissionRow,
  buildPermissionRows,
  changedPermissions,
  copyPermissionsFrom,
  effectiveFlags,
  isImpliedFlag,
  resetPermissionRows,
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
 * (Plan A Task 15). Admin locks the other flags as implied. "Kopiér fra anden
 * ejendom" is client-side and lands as unsaved changes (spec §8). Switching
 * property drops unsaved changes (Plan E decision 9), and an answer for a
 * property that is no longer selected is ignored.
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
export class ChemicalPermissionsPageComponent implements OnInit {
  private store = inject(Store);
  private chemicalsService = inject(BackendConfigurationPnChemicalsService);
  private propertiesService = inject(BackendConfigurationPnPropertiesService);
  private destroyRef = inject(DestroyRef);

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
  /** The last save was refused or failed; the edits are kept for a retry. */
  saveFailed = false;
  /** The server's reason for a refused save ('' when the request itself failed). */
  saveError = '';

  ngOnInit(): void {
    this.propertiesService.getAllPropertiesDictionary()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((res) => (this.properties = (res && res.success && res.model) || []));

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

  get otherProperties(): CommonDictionaryModel[] {
    return this.properties.filter((p) => p.id !== this.propertyId);
  }

  get propertyName(): string {
    return this.properties.find((p) => p.id === this.propertyId)?.name ?? '';
  }

  get visibleRows(): ChemicalPermissionRow[] {
    const needle = this.search.trim().toLowerCase();
    return needle ? this.rows.filter((r) => r.workerName.toLowerCase().includes(needle)) : this.rows;
  }

  get changeCount(): number {
    return changedPermissions(this.rows).length;
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
    if (propertyId === null || !changes.length || this.saving) {
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

  copyFrom(sourcePropertyId: number): void {
    const propertyId = this.propertyId;
    if (propertyId === null) {
      return;
    }
    this.chemicalsService.getPermissions(sourcePropertyId).subscribe((res) => {
      if (propertyId === this.propertyId && res && res.success) {
        this.rows = copyPermissionsFrom(this.rows, res.model);
      }
    });
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
