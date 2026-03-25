import {Component, EventEmitter, Input, Output} from '@angular/core';
import {MatDialog} from '@angular/material/dialog';
import {CommonDictionaryModel, SharedTagModel} from 'src/app/common/models';
import {CalendarBoardModel, CALENDAR_COLORS} from '../../../../models/calendar';
import {TeamCreateDialogComponent} from './team-create-dialog.component';
import {TeamDeleteDialogComponent} from './team-delete-dialog.component';

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
  @Output() createTeam = new EventEmitter<string>();
  @Output() updateTeam = new EventEmitter<{id: number; name: string}>();
  @Output() deleteTeam = new EventEmitter<number>();
  @Output() createEmployee = new EventEmitter<void>();
  @Output() createTag = new EventEmitter<string>();
  @Output() updateTag = new EventEmitter<SharedTagModel>();
  @Output() updateBoard = new EventEmitter<{id: number; name: string; color: string}>();
  @Output() deleteBoard = new EventEmitter<CalendarBoardModel>();
  @Output() deleteTag = new EventEmitter<number>();

  tagSort: 'popularity' | 'alphabetical' = 'popularity';
  newTagName = '';
  editingTagId: number | null = null;
  editingTagName = '';
  editingTeamId: number | null = null;
  editingTeamName = '';
  boardColors = CALENDAR_COLORS;
  editingBoardId: number | null = null;
  editingBoardName = '';
  editingBoardColor = '';

  constructor(private dialog: MatDialog) {}

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

  openCreateTeamDialog() {
    const dialogRef = this.dialog.open(TeamCreateDialogComponent, {width: '360px'});
    dialogRef.afterClosed().subscribe(name => {
      if (name) this.createTeam.emit(name);
    });
  }

  startEditTeam(team: CommonDictionaryModel) {
    this.editingTeamId = team.id;
    this.editingTeamName = team.name;
  }

  submitEditTeam() {
    if (this.editingTeamId !== null && this.editingTeamName.trim()) {
      this.updateTeam.emit({id: this.editingTeamId, name: this.editingTeamName.trim()});
    }
    this.editingTeamId = null;
  }

  onDeleteTeam(team: CommonDictionaryModel) {
    const dialogRef = this.dialog.open(TeamDeleteDialogComponent, {
      disableClose: true,
      minWidth: 300,
      data: {id: team.id, name: team.name},
    });
    dialogRef.afterClosed().subscribe(deletedId => {
      if (deletedId) this.deleteTeam.emit(deletedId);
    });
  }

  startEditBoard(board: CalendarBoardModel) {
    this.editingBoardId = board.id;
    this.editingBoardName = board.name;
    this.editingBoardColor = board.color;
  }

  submitEditBoard() {
    if (this.editingBoardId !== null && this.editingBoardName.trim()) {
      this.updateBoard.emit({id: this.editingBoardId, name: this.editingBoardName.trim(), color: this.editingBoardColor});
    }
    this.editingBoardId = null;
  }

  onDeleteBoard(board: CalendarBoardModel) {
    this.deleteBoard.emit(board);
  }

  emitCreateTag(): void {
    if (this.newTagName.trim()) {
      this.createTag.emit(this.newTagName.trim());
      this.newTagName = '';
    }
  }
}
