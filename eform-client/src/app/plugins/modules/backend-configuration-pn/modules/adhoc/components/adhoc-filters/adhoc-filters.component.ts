import {Component, ElementRef, HostListener, OnChanges, OnDestroy, OnInit, Input, SimpleChanges} from '@angular/core';
import {FormControl} from '@angular/forms';
import {TranslateService} from '@ngx-translate/core';
import {MatDialog} from '@angular/material/dialog';
import {Subscription, debounceTime, distinctUntilChanged} from 'rxjs';
import {AutoUnsubscribe} from 'ngx-auto-unsubscribe';
import {AdhocAreaModel, AdhocTagModel} from '../../../../models';
import {AdhocFiltrationModel} from '../../../../state';
import {BackendConfigurationPnAdhocService} from '../../../../services';
import {AdhocStateService} from '../store';
import {AdhocAreaCreateModalComponent} from '../adhoc-area-create-modal/adhoc-area-create-modal.component';
import {AdhocAreaAdminModalComponent} from '../adhoc-area-admin-modal/adhoc-area-admin-modal.component';

interface AdhocStatusOption {
  value: AdhocFiltrationModel['status'];
  label: string;
}

/**
 * Toolbar filters for the "Adhoc overblik" table (M5/F6), rendered inline
 * in the container's `eform-sub-header` card via the plugin's
 * `.filters-inline` display:contents convention (see
 * task-management-filters / documents-filters): tag filter popup (OG/ELLER + inline
 * create/delete - "Administrer/opret tags" per the plan, kept inline
 * rather than as separate modal components since none are named in the F6
 * file list), debounced search, property select, dependent area select,
 * and a status select showing the P2 index-response counts
 * ("Åbne (12)"/"Løste (4)"/"Arkiverede (1)").
 *
 * `EformSharedTagsModule`'s `EformsTagsComponent` was considered (per the
 * plan's own VERIFY-AT-EXECUTION note) but doesn't fit: it drives a
 * flat-list tag-management dialog against the global `EformTagService`,
 * with no AND/OR filter semantics and no notion of "selected for this
 * filter" - building a small custom popup here is simpler and correct.
 */
@AutoUnsubscribe()
@Component({
  selector: 'app-adhoc-filters',
  templateUrl: './adhoc-filters.component.html',
  styleUrls: ['./adhoc-filters.component.scss'],
  standalone: false,
})
export class AdhocFiltersComponent implements OnInit, OnChanges, OnDestroy {
  @Input() counts = {open: 0, completed: 0, archived: 0};

  searchControl = new FormControl('');
  tagPopupOpen = false;
  newTagName = '';
  areas: AdhocAreaModel[] = [];

  // Bound to the status mtx-select's `[items]`. Computed once per actual
  // `counts` change (`ngOnChanges`) rather than as a getter re-evaluated on
  // every change-detection pass - `[items]="someGetter()"` would hand
  // ng-select a brand-new array reference on nearly every CD tick (mouse
  // moves, keystrokes, unrelated store emissions), and ng-select's own
  // `ngOnChanges` treats any `items` reference change as a full items-list
  // reset (`_setItems`), which can null out an in-flight option click before
  // its mouseup/select handler runs - e.g. selecting a status option would
  // silently no-op with the dropdown panel left open. Memoizing this array
  // keeps the reference stable across CD ticks that don't actually change
  // `counts`, so an option click only ever races a *real* items update.
  statusOptions: AdhocStatusOption[] = [];

  searchSub$: Subscription;
  areasSub$: Subscription;
  createTagSub$: Subscription;
  deleteTagSub$: Subscription;
  loadTagsSub$: Subscription;

  constructor(
    public adhocStateService: AdhocStateService,
    private adhocService: BackendConfigurationPnAdhocService,
    private translate: TranslateService,
    private elementRef: ElementRef,
    private dialog: MatDialog,
  ) {
  }

  get currentFilters(): AdhocFiltrationModel {
    return this.adhocStateService.currentFilters;
  }

  private computeStatusOptions(): void {
    this.statusOptions = [
      {value: 'open', label: `${this.translate.instant('Open')} (${this.counts.open})`},
      {value: 'completed', label: `${this.translate.instant('Solved')} (${this.counts.completed})`},
      {value: 'archived', label: `${this.translate.instant('Archived')} (${this.counts.archived})`},
    ];
  }

  ngOnInit(): void {
    this.computeStatusOptions();

    this.searchControl.setValue(this.currentFilters.search, {emitEvent: false});
    this.searchSub$ = this.searchControl.valueChanges
      .pipe(debounceTime(500), distinctUntilChanged())
      .subscribe((value) => this.adhocStateService.updateFilters({search: value ?? ''}));

    if (this.currentFilters.propertyId != null) {
      this.loadAreas(this.currentFilters.propertyId);
    }
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['counts']) {
      this.computeStatusOptions();
    }
  }

  ngOnDestroy(): void {
  }

  @HostListener('document:click', ['$event'])
  onDocumentClick(event: MouseEvent): void {
    if (this.tagPopupOpen && !this.elementRef.nativeElement.contains(event.target)) {
      this.tagPopupOpen = false;
    }
  }

  // -----------------------------------------------------------------
  // Property / area / status
  // -----------------------------------------------------------------

  onPropertyChange(propertyId: number | null): void {
    this.adhocStateService.updateFilters({propertyId, areaId: null});
    if (propertyId != null) {
      this.loadAreas(propertyId);
    } else {
      this.areas = [];
    }
  }

  private loadAreas(propertyId: number): void {
    this.areasSub$ = this.adhocStateService.getAreasForProperty(propertyId).subscribe((areas) => (this.areas = areas));
  }

  onAreaChange(areaId: number | null): void {
    this.adhocStateService.updateFilters({areaId});
  }

  openAreaCreateModal(): void {
    const propertyId = this.currentFilters.propertyId;
    if (propertyId == null) {
      return;
    }
    this.dialog
      .open(AdhocAreaCreateModalComponent, {
        minWidth: 400,
        data: {propertyId, propertyName: this.propertyName(propertyId)},
      })
      .afterClosed()
      .subscribe((changed) => {
        if (changed) {
          this.loadAreas(propertyId);
        }
      });
  }

  openAreaAdminModal(): void {
    const propertyId = this.currentFilters.propertyId;
    if (propertyId == null) {
      return;
    }
    this.dialog
      .open(AdhocAreaAdminModalComponent, {
        minWidth: 440,
        data: {propertyId, propertyName: this.propertyName(propertyId)},
      })
      .afterClosed()
      .subscribe((changed) => {
        if (changed) {
          this.loadAreas(propertyId);
        }
      });
  }

  private propertyName(propertyId: number): string {
    return this.adhocStateService.properties.find((p) => p.id === propertyId)?.name ?? '';
  }

  onStatusChange(status: AdhocFiltrationModel['status']): void {
    this.adhocStateService.updateFilters({status});
  }

  // -----------------------------------------------------------------
  // Tags (filter popup: OG/ELLER + inline create/delete)
  // -----------------------------------------------------------------

  toggleTagPopup(): void {
    this.tagPopupOpen = !this.tagPopupOpen;
  }

  isTagSelected(tagId: number): boolean {
    return this.currentFilters.tagIds.includes(tagId);
  }

  onToggleTag(tagId: number): void {
    const current = this.currentFilters.tagIds;
    const next = current.includes(tagId) ? current.filter((id) => id !== tagId) : [...current, tagId];
    this.adhocStateService.updateFilters({tagIds: next});
  }

  onTagLogicChange(tagLogic: AdhocFiltrationModel['tagLogic']): void {
    this.adhocStateService.updateFilters({tagLogic});
  }

  onCreateTag(): void {
    const name = this.newTagName.trim();
    if (!name) {
      return;
    }
    this.createTagSub$ = this.adhocService.createTag(name).subscribe((res) => {
      if (res && res.success) {
        this.newTagName = '';
        this.loadTagsSub$ = this.adhocStateService.loadTags().subscribe();
      }
    });
  }

  onDeleteTag(tag: AdhocTagModel): void {
    this.deleteTagSub$ = this.adhocService.deleteTag(tag.id).subscribe((res) => {
      if (res && res.success) {
        if (this.isTagSelected(tag.id)) {
          this.onToggleTag(tag.id);
        }
        this.loadTagsSub$ = this.adhocStateService.loadTags().subscribe();
      }
    });
  }
}
