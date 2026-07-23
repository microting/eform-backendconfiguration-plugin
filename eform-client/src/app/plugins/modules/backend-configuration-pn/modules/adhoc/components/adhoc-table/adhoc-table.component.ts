import {Component, EventEmitter, Input, OnChanges, Output, SimpleChanges, inject} from '@angular/core';
import {MtxGridColumn} from '@ng-matero/extensions/grid';
import {TranslateService} from '@ngx-translate/core';
import {Sort} from '@angular/material/sort';
import {Store} from '@ngrx/store';
import {AdhocTaskModel} from '../../../../models';
import {
  AdhocFiltrationModel,
  selectAdhocPaginationIsSortDsc,
  selectAdhocPaginationSort,
} from '../../../../state';
import {AdhocStateService} from '../store';
import {
  AssignedToDisplay,
  DEADLINE_BAND_COLORS,
  DeadlineVisual,
  assignedToDisplay,
  deadlineVisual,
  resolveAreaName,
  resolvePropertyName,
  resolveTagNames,
  resolveWorkerName,
} from '../../adhoc-display.util';

/**
 * "Adhoc overblik" table (M5/F5) - mtx-grid over the current page of
 * `AdhocTaskModel` rows + a column picker. Since `AdhocTaskModel` carries
 * ids only (no propertyName/areaName/createdByName/tag or worker names -
 * see adhoc-display.util.ts's header comment), every "name" cell resolves
 * client-side from `AdhocStateService`'s reference-data caches
 * (properties/tags global; areas/workers loaded lazily per the row's
 * `propertyId` and cached for the container's lifetime).
 *
 * Row actions (Vis/Rediger/Kopier/Slet) and the "Udfør" trigger on open
 * rows' status cell are stubbed here pending the drawer (F7) and modals
 * (F8) - same TODO-stub pattern F4 used for the container's "Ny opgave"
 * button.
 */
@Component({
  selector: 'app-adhoc-table',
  templateUrl: './adhoc-table.component.html',
  styleUrls: ['./adhoc-table.component.scss'],
  standalone: false,
})
export class AdhocTableComponent implements OnChanges {
  private store = inject(Store);
  private translateService = inject(TranslateService);
  public adhocStateService = inject(AdhocStateService);

  @Input() tasks: AdhocTaskModel[] = [];
  @Input() status: AdhocFiltrationModel['status'] = 'open';
  @Output() updateTable: EventEmitter<void> = new EventEmitter<void>();
  @Output() viewTask: EventEmitter<AdhocTaskModel> = new EventEmitter<AdhocTaskModel>();
  @Output() editTask: EventEmitter<AdhocTaskModel> = new EventEmitter<AdhocTaskModel>();
  @Output() copyTask: EventEmitter<AdhocTaskModel> = new EventEmitter<AdhocTaskModel>();
  @Output() deleteTask: EventEmitter<AdhocTaskModel> = new EventEmitter<AdhocTaskModel>();
  @Output() completeTask: EventEmitter<AdhocTaskModel> = new EventEmitter<AdhocTaskModel>();

  public selectAdhocPaginationSort$ = this.store.select(selectAdhocPaginationSort);
  public selectAdhocPaginationIsSortDsc$ = this.store.select(selectAdhocPaginationIsSortDsc);

  columnPickerOpen = false;

  tableHeaders: MtxGridColumn[] = [
    {header: this.translateService.stream('Task'), field: 'title', sortProp: {id: 'Title'}, sortable: true, class: 'title'},
    {header: this.translateService.stream('Content'), field: 'content', class: 'content'},
    {header: this.translateService.stream('Property'), field: 'propertyName', class: 'propertyName'},
    {header: this.translateService.stream('Area'), field: 'areaName', class: 'areaName'},
    {header: this.translateService.stream('Tags'), field: 'tags', class: 'tags'},
    {header: this.translateService.stream('Created by'), field: 'createdByName', class: 'createdByName'},
    {header: this.translateService.stream('Assigned to'), field: 'assignedTo', class: 'assignedTo'},
    {header: this.translateService.stream('Last deadline'), field: 'deadline', sortProp: {id: 'Deadline'}, sortable: true, class: 'deadline'},
    {header: this.translateService.stream('Completed by'), field: 'completedByName', class: 'completedByName'},
    {header: this.translateService.stream('Created'), field: 'createdAt', sortProp: {id: 'CreatedAt'}, sortable: true, type: 'date', typeParameter: {format: 'dd.MM.yyyy'}, class: 'createdAt'},
    {header: this.translateService.stream('Status'), field: 'status', class: 'status'},
    {header: this.translateService.stream('Actions'), field: 'actions', width: '90px', pinned: 'right', right: '0px'},
  ];

  /** Column fields the user may hide via the column picker (id/actions/title are always shown). */
  toggleableFields = [
    'content', 'propertyName', 'areaName', 'tags', 'createdByName', 'assignedTo',
    'deadline', 'completedByName', 'createdAt', 'status',
  ];

  get toggleableColumns(): MtxGridColumn[] {
    return this.tableHeaders.filter((c) => this.toggleableFields.includes(c.field));
  }

  get visibleColumns(): MtxGridColumn[] {
    const hidden = this.adhocStateService.currentHiddenColumns ?? [];
    return this.tableHeaders.filter((c) => {
      if (c.field === 'completedByName') {
        // Mockup: "Udført af" only appears for Løste/Arkiverede.
        return this.status !== 'open' && !hidden.includes(c.field);
      }
      return !hidden.includes(c.field);
    });
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['tasks']) {
      this.loadReferenceDataForVisibleRows();
    }
  }

  /** Loads areas/workers for every distinct propertyId on the current page (cached - see AdhocStateService). */
  private loadReferenceDataForVisibleRows(): void {
    const propertyIds = Array.from(new Set((this.tasks ?? []).map((t) => t.propertyId)));
    for (const propertyId of propertyIds) {
      this.adhocStateService.getAreasForProperty(propertyId).subscribe();
      this.adhocStateService.getWorkersForProperty(propertyId).subscribe();
    }
  }

  // -----------------------------------------------------------------
  // Name resolution (template helpers)
  // -----------------------------------------------------------------

  propertyNameFor(task: AdhocTaskModel): string {
    return resolvePropertyName(this.adhocStateService.properties, task.propertyId);
  }

  areaNameFor(task: AdhocTaskModel): string {
    return resolveAreaName(this.adhocStateService.getCachedAreas(task.propertyId), task.areaId);
  }

  createdByNameFor(task: AdhocTaskModel): string {
    return resolveWorkerName(this.adhocStateService.getCachedWorkers(task.propertyId), task.createdByWorkerId);
  }

  completedByNameFor(task: AdhocTaskModel): string {
    return resolveWorkerName(this.adhocStateService.getCachedWorkers(task.propertyId), task.completedByWorkerId);
  }

  tagNamesFor(task: AdhocTaskModel): string[] {
    return resolveTagNames(this.adhocStateService.tags, task.tagIds);
  }

  assignedToFor(task: AdhocTaskModel): AssignedToDisplay {
    return assignedToDisplay(task.executionRule, task.assignedWorkerIds, this.adhocStateService.getCachedWorkers(task.propertyId));
  }

  deadlineVisualFor(task: AdhocTaskModel): DeadlineVisual {
    return deadlineVisual(task.deadline);
  }

  deadlineColor(band: DeadlineVisual['band']): string {
    return DEADLINE_BAND_COLORS[band];
  }

  // -----------------------------------------------------------------
  // Sorting / column picker
  // -----------------------------------------------------------------

  sortTable(sort: Sort): void {
    this.adhocStateService.onSortTable(sort.active);
    this.updateTable.emit();
  }

  toggleColumnPicker(): void {
    if (this.status !== 'open') {
      return;
    }
    this.columnPickerOpen = !this.columnPickerOpen;
  }

  closeColumnPicker(): void {
    this.columnPickerOpen = false;
  }

  isColumnHidden(field: string): boolean {
    return (this.adhocStateService.currentHiddenColumns ?? []).includes(field);
  }

  onToggleColumn(field: string): void {
    const current = this.adhocStateService.currentHiddenColumns ?? [];
    const next = current.includes(field) ? current.filter((f) => f !== field) : [...current, field];
    this.adhocStateService.updateHiddenColumns(next);
  }

  // -----------------------------------------------------------------
  // Row actions (wired up by F7/F8)
  // -----------------------------------------------------------------

  onViewTask(task: AdhocTaskModel): void {
    this.viewTask.emit(task);
  }

  onEditTask(task: AdhocTaskModel): void {
    this.editTask.emit(task);
  }

  onCopyTask(task: AdhocTaskModel): void {
    this.copyTask.emit(task);
  }

  onDeleteTask(task: AdhocTaskModel): void {
    this.deleteTask.emit(task);
  }

  onCompleteTask(task: AdhocTaskModel): void {
    this.completeTask.emit(task);
  }
}
