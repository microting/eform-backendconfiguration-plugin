import {Component, OnDestroy, OnInit} from '@angular/core';
import {MatDialog} from '@angular/material/dialog';
import {Overlay} from '@angular/cdk/overlay';
import {Subject} from 'rxjs';
import {takeUntil} from 'rxjs/operators';
import {dialogConfigHelper} from 'src/app/common/helpers';
import {
  BackendConfigurationPnCalendarService,
  BackendConfigurationPnPropertiesService,
} from '../../../../services';
import {
  CalendarBoardModel,
  CalendarTaskLayoutModel,
  CalendarTaskModel,
} from '../../../../models/calendar';
import {CommonDictionaryModel, SharedTagModel} from 'src/app/common/models';
import {CalendarLayoutService} from '../../services/calendar-layout.service';
import {CalendarStateService} from '../store';
import {TaskCreateEditModalComponent} from '../../modals/task-create-edit-modal/task-create-edit-modal.component';
import {ItemsPlanningPnTagsService} from 'src/app/plugins/modules/items-planning-pn/services';

@Component({
  standalone: false,
  selector: 'app-calendar-container',
  templateUrl: './calendar-container.component.html',
  styleUrls: ['./calendar-container.component.scss'],
})
export class CalendarContainerComponent implements OnInit, OnDestroy {
  private destroy$ = new Subject<void>();

  properties: CommonDictionaryModel[] = [];
  boards: CalendarBoardModel[] = [];
  teams: CommonDictionaryModel[] = [];
  employees: CommonDictionaryModel[] = [];
  tags: SharedTagModel[] = [];
  tasks: CalendarTaskModel[] = [];
  tasksByDay: CalendarTaskLayoutModel[][] = Array.from({length: 7}, () => []);

  currentPropertyId: number | null = null;
  currentDate: string = new Date().toISOString().split('T')[0];
  viewMode: 'week' | 'day' | 'schedule' = 'week';
  activeBoardIds: number[] = [];
  activeTagNames: string[] = [];
  sidebarOpen = true;

  constructor(
    private dialog: MatDialog,
    private overlay: Overlay,
    private calendarService: BackendConfigurationPnCalendarService,
    private propertiesService: BackendConfigurationPnPropertiesService,
    private layoutService: CalendarLayoutService,
    private stateService: CalendarStateService,
    private tagsService: ItemsPlanningPnTagsService,
  ) {}

  ngOnInit(): void {
    this.stateService.filters$.pipe(takeUntil(this.destroy$)).subscribe(filters => {
      this.currentPropertyId = filters.propertyId;
      this.currentDate = filters.currentDate;
      this.viewMode = filters.viewMode;
      this.activeBoardIds = filters.activeBoardIds;
      this.activeTagNames = filters.activeTagNames;
      this.sidebarOpen = filters.sidebarOpen;
    });

    this.loadProperties();
    this.loadEmployees();
    this.loadTags();
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  loadProperties() {
    this.propertiesService.getAllPropertiesDictionary().subscribe(res => {
      if (res && res.success) {
        this.properties = res.model;
      }
    });
  }

  onPropertySelected(propertyId: number | null) {
    this.stateService.updatePropertyId(propertyId);
    if (propertyId) {
      this.loadBoards(propertyId);
      this.loadTasks();
    }
  }

  loadBoards(propertyId: number) {
    this.calendarService.getBoards(propertyId).subscribe(res => {
      if (res && res.success) this.boards = res.model;
    });
  }

  loadTags() {
    this.tagsService.getPlanningsTags().subscribe(res => {
      if (res && res.success) this.tags = res.model;
    });
  }

  onCreateTag(name: string) {
    if (!name.trim()) return;
    this.tagsService.createPlanningTag({name: name.trim()}).subscribe(res => {
      if (res && res.success) this.loadTags();
    });
  }

  onUpdateTag(tag: SharedTagModel) {
    this.tagsService.updatePlanningTag(tag).subscribe(res => {
      if (res && res.success) this.loadTags();
    });
  }

  onDeleteTag(id: number) {
    this.tagsService.deletePlanningTag(id).subscribe(res => {
      if (res && res.success) this.loadTags();
    });
  }

  loadEmployees() {
    this.propertiesService.getDeviceUsersFiltered({
      propertyIds: [],
      nameFilter: '',
      sort: 'Name',
      isSortDsc: false,
      showResigned: false,
      tagIds: [],
    }).subscribe(res => {
      if (res && res.success) {
        this.employees = res.model.map(u => ({
          id: u.siteId,
          name: u.fullName || `${u.userFirstName} ${u.userLastName}`.trim() || u.siteName,
          description: '',
        } as CommonDictionaryModel));
      }
    });
  }

  loadTasks() {
    if (!this.currentPropertyId) return;

    const monday = this.getMondayOfWeek(new Date(this.currentDate));
    const sunday = new Date(monday);
    sunday.setDate(monday.getDate() + 6);
    const weekStart = monday.toISOString().split('T')[0];
    const weekEnd = sunday.toISOString().split('T')[0];

    this.calendarService
      .getTasksForWeek(
        this.currentPropertyId,
        weekStart,
        weekEnd,
        this.activeBoardIds,
        this.activeTagNames,
      )
      .subscribe(res => {
        if (res && res.success) {
          this.tasks = res.model;
          this.rebuildLayout(monday);
        }
      });
  }

  private rebuildLayout(monday: Date) {
    this.tasksByDay = Array.from({length: 7}, () => []);
    this.tasks.forEach(task => {
      const taskDate = new Date(task.taskDate);
      taskDate.setHours(0, 0, 0, 0);
      const mondayCopy = new Date(monday);
      mondayCopy.setHours(0, 0, 0, 0);
      const dayIdx = Math.round((taskDate.getTime() - mondayCopy.getTime()) / 86400000);
      if (dayIdx >= 0 && dayIdx < 7) {
        this.tasksByDay[dayIdx].push({...task, _colIndex: 0, _colCount: 1});
      }
    });
    this.tasksByDay = this.tasksByDay.map(dayTasks =>
      this.layoutService.computeLayout(dayTasks)
    );
  }

  openCreateModal(date: string, startHour: number) {
    const dialogRef = this.dialog.open(
      TaskCreateEditModalComponent,
      dialogConfigHelper(this.overlay, {
        task: null,
        date,
        startHour,
        boards: this.boards,
        employees: this.employees,
        tags: this.tags.map(t => t.name),
        propertyId: this.currentPropertyId,
      })
    );
    dialogRef.afterClosed().pipe(takeUntil(this.destroy$)).subscribe(result => {
      if (result) this.loadTasks();
    });
  }

  onNavigate(direction: -1 | 1) {
    const d = new Date(this.currentDate);
    d.setDate(d.getDate() + direction * 7);
    this.stateService.updateCurrentDate(d.toISOString().split('T')[0]);
    this.loadTasks();
  }

  onGoToToday() {
    this.stateService.updateCurrentDate(new Date().toISOString().split('T')[0]);
    this.loadTasks();
  }

  onViewModeChange(viewMode: 'week' | 'day' | 'schedule') {
    this.stateService.updateViewMode(viewMode);
  }

  onToggleSidebar() {
    this.stateService.toggleSidebar();
  }

  onBoardToggled(boardId: number) {
    this.stateService.toggleBoard(boardId);
    this.loadTasks();
  }

  onTagToggled(tagName: string) {
    this.stateService.toggleTag(tagName);
    this.loadTasks();
  }

  onTaskMoved(event: {taskId: number; newDate: string; newStartHour: number}) {
    this.calendarService
      .moveTask(event.taskId, event.newDate, event.newStartHour)
      .subscribe(res => {
        if (res && res.success) this.loadTasks();
      });
  }

  private getMondayOfWeek(d: Date): Date {
    const date = new Date(d);
    const day = date.getDay();
    const diff = day === 0 ? -6 : 1 - day;
    date.setDate(date.getDate() + diff);
    date.setHours(0, 0, 0, 0);
    return date;
  }
}
