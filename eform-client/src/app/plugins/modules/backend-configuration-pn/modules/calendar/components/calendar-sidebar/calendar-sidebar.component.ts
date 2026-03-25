import {Component, EventEmitter, Input, Output} from '@angular/core';
import {CommonDictionaryModel, SharedTagModel} from 'src/app/common/models';
import {CalendarBoardModel} from '../../../../models/calendar';

@Component({
  standalone: false,
  selector: 'app-calendar-sidebar',
  templateUrl: './calendar-sidebar.component.html',
  styleUrls: ['./calendar-sidebar.component.scss'],
})
export class CalendarSidebarComponent {
  @Input() properties: CommonDictionaryModel[] = [];
  @Input() boards: CalendarBoardModel[] = [];
  @Input() teams: CommonDictionaryModel[] = [];
  @Input() employees: CommonDictionaryModel[] = [];
  @Input() tags: SharedTagModel[] = [];
  @Input() activeBoardIds: number[] = [];
  @Input() activeSiteIds: number[] = [];
  @Input() activeTagNames: string[] = [];
  @Input() selectedPropertyId: number | null = null;

  @Output() propertySelected = new EventEmitter<number | null>();
  @Output() boardToggled = new EventEmitter<number>();
  @Output() employeeToggled = new EventEmitter<number>();
  @Output() tagToggled = new EventEmitter<string>();
  @Output() createProperty = new EventEmitter<void>();
  @Output() createBoard = new EventEmitter<void>();
  @Output() createTeam = new EventEmitter<void>();
  @Output() createEmployee = new EventEmitter<void>();
  @Output() createTag = new EventEmitter<string>();
  @Output() updateTag = new EventEmitter<SharedTagModel>();
  @Output() deleteTag = new EventEmitter<number>();

  tagSort: 'popularity' | 'alphabetical' = 'popularity';
  newTagName = '';
  editingTagId: number | null = null;
  editingTagName = '';

  get sortedTags(): SharedTagModel[] {
    if (this.tagSort === 'alphabetical') {
      return [...this.tags].sort((a, b) => a.name.localeCompare(b.name));
    }
    return this.tags;
  }

  isBoardActive(boardId: number): boolean {
    return this.activeBoardIds.includes(boardId);
  }

  isEmployeeActive(siteId: number): boolean {
    return this.activeSiteIds.includes(siteId);
  }

  isTagActive(tag: SharedTagModel): boolean {
    return this.activeTagNames.includes(tag.name);
  }

  startEdit(tag: SharedTagModel): void {
    this.editingTagId = tag.id;
    this.editingTagName = tag.name;
  }

  confirmEdit(tag: SharedTagModel): void {
    if (this.editingTagName.trim()) {
      this.updateTag.emit({...tag, name: this.editingTagName.trim()});
    }
    this.editingTagId = null;
  }

  emitCreateTag(): void {
    if (this.newTagName.trim()) {
      this.createTag.emit(this.newTagName.trim());
      this.newTagName = '';
    }
  }
}
