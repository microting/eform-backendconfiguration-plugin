import {Component, EventEmitter, Input, OnDestroy, OnInit, Output} from '@angular/core';
import {Subject} from 'rxjs';
import {debounceTime, takeUntil} from 'rxjs/operators';
import {CommonDictionaryModel, SharedTagModel} from 'src/app/common/models';
import {CalendarBoardModel, CalendarTaskListFiltrationModel} from '../../../../models/calendar';

@Component({
  selector: 'app-calendar-task-list-filters',
  templateUrl: './calendar-task-list-filters.component.html',
  styleUrls: ['./calendar-task-list-filters.component.scss'],
  standalone: false,
})
export class CalendarTaskListFiltersComponent implements OnInit, OnDestroy {
  @Input() properties: CommonDictionaryModel[] = [];
  @Input() boards: CalendarBoardModel[] = [];
  @Input() eforms: {id: number; label: string}[] = [];
  @Input() workers: CommonDictionaryModel[] = [];
  @Input() tags: SharedTagModel[] = [];
  @Output() filtersChanged = new EventEmitter<CalendarTaskListFiltrationModel>();
  @Output() propertyChanged = new EventEmitter<number | null>();

  filters: CalendarTaskListFiltrationModel = {
    propertyIds: [], boardIds: [], eformIds: [], assignToIds: [],
    tagIds: [], status: null, complianceEnabled: null, nameFilter: null,
  };

  private searchChanged = new Subject<string>();
  private destroy$ = new Subject<void>();

  ngOnInit() {
    this.searchChanged.pipe(debounceTime(300), takeUntil(this.destroy$)).subscribe(() => this.emit());
  }

  ngOnDestroy() {
    this.destroy$.next();
    this.destroy$.complete();
  }

  emit() {
    this.filtersChanged.emit({...this.filters});
  }

  onSearchChange(value: string) {
    this.filters.nameFilter = value;
    this.searchChanged.next(value);
  }

  onPropertyChange(ids: number[]) {
    this.filters.propertyIds = ids ?? [];
    // Calendar/worker option-lists depend on a single chosen property; clear the
    // selections that no longer apply when the property set changes.
    if (this.filters.propertyIds.length !== 1) {
      this.filters.boardIds = [];
      this.filters.assignToIds = [];
    }
    this.propertyChanged.emit(this.filters.propertyIds.length === 1 ? this.filters.propertyIds[0] : null);
    this.emit();
  }
}
