import {Component, EventEmitter, Input, OnChanges, OnDestroy, Output, SimpleChanges, inject} from '@angular/core';
import {TranslateService, TranslatePipe} from '@ngx-translate/core';
import {Paged} from 'src/app/common/models';
import {DocumentDateState, FilesModel} from '../../../../models';
import {BackendConfigurationPnFilesService} from '../../../../services';
import {ARCHIVE_PATH_SEPARATOR, dateOnlyOf, whoCanRead} from '../../../../helpers';
import {NgIf, NgFor, NgSwitch, NgSwitchCase, NgSwitchDefault, DatePipe} from '@angular/common';
import {MatIcon} from '@angular/material/icon';
import {MatIconButton} from '@angular/material/button';
import {MatTooltip} from '@angular/material/tooltip';
import {MatMenu, MatMenuItem, MatMenuTrigger} from '@angular/material/menu';
import {MatCheckbox} from '@angular/material/checkbox';

/**
 * The W1 document table: Checkbox · Navn · Dokumentdato · Ejendomme · Læseadgang (admin) · Etiketter.
 * A native table so rows can be HTML5 drag sources for the folder tree. Selection is owned by the page
 * (`selectedIds`) and emitted in table order, so "1 af 2 valgte" follows the rows.
 */
@Component({
  selector: 'app-files-table',
  imports: [NgIf, NgFor, NgSwitch, NgSwitchCase, NgSwitchDefault, DatePipe, TranslatePipe, MatIcon, MatIconButton, MatTooltip, MatMenu, MatMenuItem, MatMenuTrigger, MatCheckbox],
  templateUrl: './files-table.component.html',
})
export class FilesTableComponent implements OnChanges, OnDestroy {
  private filesService = inject(BackendConfigurationPnFilesService);
  private translateService = inject(TranslateService);

  @Input() files: Paged<FilesModel> = new Paged<FilesModel>();
  @Input() isAdmin = false;
  /** Search and "Dato skal tjekkes" span all folders: then each name shows its folder path. */
  @Input() showPath = false;
  @Input() loaded = false;
  @Input() selectedIds: number[] = [];
  @Input() currentFileId: number | null = null;
  @Input() highlightedFileId: number | null = null;
  @Input() sortField = '';
  @Input() sortDesc = false;
  @Output() selectedIdsChange = new EventEmitter<number[]>();
  @Output() openFile = new EventEmitter<FilesModel>();
  @Output() openDeleteModal = new EventEmitter<FilesModel>();
  @Output() rowDragStart = new EventEmitter<FilesModel>();
  @Output() rowDragEnd = new EventEmitter<void>();
  @Output() sort = new EventEmitter<string>();

  readonly DateState = DocumentDateState;
  rows: FilesModel[] = [];
  /** The blob URL of the last opened PDF; revoked when the next one opens or the table goes away. */
  private viewUrl: string | null = null;

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['files']) {
      this.rows = (this.files?.entities ?? []).map(file => ({
        ...file,
        tags: [...(file.tags ?? [])].sort((a, b) => a.name.localeCompare(b.name, undefined, {sensitivity: 'base'})),
      }));
    }
  }

  ngOnDestroy(): void {
    this.revokeViewUrl();
  }

  trackById(_: number, row: FilesModel): number {
    return row.id;
  }

  /** Enter or Space on a focused row opens it, like a click. Keys on the checkbox or the menu button are theirs. */
  onRowKey(event: Event, row: FilesModel): void {
    if (event.target !== event.currentTarget) {
      return;
    }
    event.preventDefault();
    this.openFile.emit(row);
  }

  get allSelected(): boolean {
    return this.rows.length > 0 && this.rows.every(r => this.selectedIds.includes(r.id));
  }

  get someSelected(): boolean {
    return this.selectedIds.length > 0 && !this.allSelected;
  }

  isSelected(id: number): boolean {
    return this.selectedIds.includes(id);
  }

  toggle(id: number): void {
    const selected = new Set(this.selectedIds);
    if (selected.has(id)) {
      selected.delete(id);
    } else {
      selected.add(id);
    }
    this.selectedIdsChange.emit(this.rows.filter(r => selected.has(r.id)).map(r => r.id));
  }

  toggleAll(checked: boolean): void {
    this.selectedIdsChange.emit(checked ? this.rows.map(r => r.id) : []);
  }

  /** The server's calendar day as yyyy-MM-dd (serverDay), which the date pipe reads as a local day. */
  documentDay(row: FilesModel): string | null {
    return dateOnlyOf(row.documentDate);
  }

  pathLabel(row: FilesModel): string {
    return (row.folderPath ?? []).join(ARCHIVE_PATH_SEPARATOR);
  }

  ownAccessLabel(row: FilesModel): string {
    return whoCanRead(row.readGroups ?? [], key => this.translateService.instant(key));
  }

  onDragStart(event: DragEvent, row: FilesModel): void {
    if (event.dataTransfer) {
      // Firefox only starts a drag with data set; the page tracks the dragged ids itself.
      event.dataTransfer.setData('text/plain', String(row.id));
      event.dataTransfer.effectAllowed = 'move';
    }
    this.rowDragStart.emit(row);
  }

  onOpenView(row: FilesModel): void {
    this.filesService.getPdfFile(row.id).subscribe(blob => {
      // Not revoked at once: the new tab may still be loading it.
      this.revokeViewUrl();
      this.viewUrl = URL.createObjectURL(blob);
      window.open(this.viewUrl);
    });
  }

  private revokeViewUrl(): void {
    if (this.viewUrl) {
      URL.revokeObjectURL(this.viewUrl);
      this.viewUrl = null;
    }
  }
}
