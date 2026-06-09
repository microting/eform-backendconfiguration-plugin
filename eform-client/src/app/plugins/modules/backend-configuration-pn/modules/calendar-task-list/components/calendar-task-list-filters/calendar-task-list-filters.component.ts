import {Component, EventEmitter, Input, Output} from '@angular/core';
import {CommonDictionaryModel, SharedTagModel} from 'src/app/common/models';
import {CalendarBoardModel, CalendarTaskListFiltrationModel} from '../../../../models/calendar';

@Component({
  selector: 'app-calendar-task-list-filters',
  templateUrl: './calendar-task-list-filters.component.html',
  styleUrls: ['./calendar-task-list-filters.component.scss'],
  standalone: false,
})
export class CalendarTaskListFiltersComponent {
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

  emit() {
    this.filtersChanged.emit({...this.filters});
  }

  onPropertyChange(ids: number[]) {
    this.filters.propertyIds = ids ?? [];
    // Board/worker option-lists depend on a single chosen property; clear the
    // selections that no longer apply when the property set changes.
    if (this.filters.propertyIds.length !== 1) {
      this.filters.boardIds = [];
      this.filters.assignToIds = [];
    }
    this.propertyChanged.emit(this.filters.propertyIds.length === 1 ? this.filters.propertyIds[0] : null);
    this.emit();
  }
}
