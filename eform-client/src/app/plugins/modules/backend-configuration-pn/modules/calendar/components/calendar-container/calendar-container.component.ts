import {Component, Injector, OnDestroy, OnInit, ViewChild} from '@angular/core';
import {MatDialog} from '@angular/material/dialog';
import {Overlay, OverlayRef, ConnectedPosition} from '@angular/cdk/overlay';
import {ComponentPortal} from '@angular/cdk/portal';
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
import {MAT_DIALOG_DATA, MatDialogRef} from '@angular/material/dialog';
import {TaskCreateEditModalComponent, TaskCreateEditModalData} from '../../modals/task-create-edit-modal/task-create-edit-modal.component';
import {CalendarWeekGridComponent} from '../calendar-week-grid/calendar-week-grid.component';
import {TaskPreviewModalComponent, TaskPreviewModalData} from '../../modals/task-preview-modal/task-preview-modal.component';
import {ItemsPlanningPnTagsService} from 'src/app/plugins/modules/items-planning-pn/services';
import {RepeatScopeModalComponent} from '../../modals/repeat-scope-modal/repeat-scope-modal.component';

@Component({
  standalone: false,
  selector: 'app-calendar-container',
  templateUrl: './calendar-container.component.html',
  styleUrls: ['./calendar-container.component.scss'],
})
export class CalendarContainerComponent implements OnInit, OnDestroy {
  @ViewChild(CalendarWeekGridComponent) weekGrid?: CalendarWeekGridComponent;
  private destroy$ = new Subject<void>();
  private createOverlayRef: OverlayRef | null = null;
  private previewOverlayRef: OverlayRef | null = null;

  properties: CommonDictionaryModel[] = [];
  boards: CalendarBoardModel[] = [];
  teams: CommonDictionaryModel[] = [];
  employees: CommonDictionaryModel[] = [];
  tags: SharedTagModel[] = [];
  tasks: CalendarTaskModel[] = [];
  tasksByDay: CalendarTaskLayoutModel[][] = Array.from({length: 7}, () => []);

  get tagNames(): string[] { return this.tags.map(t => t.name); }

  currentPropertyId: number | null = null;
  currentDate: string = (() => { const d = new Date(); return `${d.getFullYear()}-${(d.getMonth()+1).toString().padStart(2,'0')}-${d.getDate().toString().padStart(2,'0')}`; })();
  viewMode: 'week' | 'day' | 'schedule' = 'week';
  activeBoardIds: number[] = [];
  activeTagNames: string[] = [];
  sidebarOpen = true;

  constructor(
    private dialog: MatDialog,
    private overlay: Overlay,
    private injector: Injector,
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
    const weekStart = this.toLocalDateString(monday);
    const weekEnd = this.toLocalDateString(sunday);

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

  openCreateModal(event: {date: string; startHour: number; cellLeft: number; cellRight: number; slotTop: number}) {
    // Close any existing popover (without clearing selection — new selection is already set)
    if (this.createOverlayRef) {
      this.createOverlayRef.dispose();
      this.createOverlayRef = null;
    }

    const positions = this.buildPopoverPositions(event.cellLeft, event.cellRight);
    const anchorX = this.pickAnchorX(event.cellLeft, event.cellRight);

    const positionStrategy = this.overlay
      .position()
      .flexibleConnectedTo({x: anchorX, y: event.slotTop})
      .withPositions(positions)
      .withPush(true)
      .withViewportMargin(8);

    this.createOverlayRef = this.overlay.create({
      positionStrategy,
      hasBackdrop: true,
      backdropClass: 'cdk-overlay-transparent-backdrop',
      scrollStrategy: this.overlay.scrollStrategies.reposition(),
    });

    const data: TaskCreateEditModalData = {
      task: null,
      date: event.date,
      startHour: event.startHour,
      boards: this.boards,
      employees: this.employees,
      tags: this.tags.map(t => t.name),
      propertyId: this.currentPropertyId!,
      properties: this.properties,
    };

    const popoverInjector = Injector.create({
      parent: this.injector,
      providers: [
        {provide: MAT_DIALOG_DATA, useValue: data},
        {provide: MatDialogRef, useValue: null},
      ],
    });

    const portal = new ComponentPortal(TaskCreateEditModalComponent, null, popoverInjector);
    const componentRef = this.createOverlayRef.attach(portal);

    componentRef.instance.usePopoverMode = true;
    componentRef.instance.timeChanged.pipe(takeUntil(this.destroy$)).subscribe(time => {
      this.weekGrid?.updateSelectionTime(time.startHour, time.endHour);
    });
    componentRef.instance.popoverClose.pipe(takeUntil(this.destroy$)).subscribe(result => {
      this.closeCreateOverlay();
      if (result) this.loadTasks();
    });

    this.createOverlayRef.backdropClick().pipe(takeUntil(this.destroy$)).subscribe(() => {
      this.closeCreateOverlay();
    });
  }

  private closeCreateOverlay() {
    this.createOverlayRef?.dispose();
    this.createOverlayRef = null;
    this.weekGrid?.clearSelection();
  }

  onNavigate(direction: -1 | 1) {
    const d = new Date(this.currentDate);
    d.setDate(d.getDate() + direction * 7);
    this.stateService.updateCurrentDate(this.toLocalDateString(d));
    this.loadTasks();
  }

  onGoToToday() {
    this.stateService.updateCurrentDate(this.toLocalDateString(new Date()));
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

  onTaskMoved(event: {taskId: number; newDate: string; newStartHour: number; repeatSeriesId?: string}) {
    if (event.repeatSeriesId) {
      // Show scope dialog for repeating tasks
      const ref = this.dialog.open(
        RepeatScopeModalComponent,
        dialogConfigHelper(this.overlay, {mode: 'move'})
      );
      ref.afterClosed().pipe(takeUntil(this.destroy$)).subscribe(scope => {
        if (scope) {
          this.calendarService
            .moveTaskWithScope(event.taskId, event.newDate, event.newStartHour, scope)
            .subscribe(res => {
              if (res && res.success) this.loadTasks();
            });
        } else {
          this.loadTasks(); // Revert visual position
        }
      });
    } else {
      this.calendarService
        .moveTask(event.taskId, event.newDate, event.newStartHour)
        .subscribe(res => {
          if (res && res.success) this.loadTasks();
        });
    }
  }

  onTaskClickedFromGrid(event: {task: CalendarTaskLayoutModel; cellLeft: number; cellRight: number; slotTop: number}) {
    this.closePreviewOverlay();

    const positions = this.buildPopoverPositions(event.cellLeft, event.cellRight);
    const anchorX = this.pickAnchorX(event.cellLeft, event.cellRight);

    const positionStrategy = this.overlay
      .position()
      .flexibleConnectedTo({x: anchorX, y: event.slotTop})
      .withPositions(positions)
      .withPush(true)
      .withViewportMargin(8);

    this.previewOverlayRef = this.overlay.create({
      positionStrategy,
      hasBackdrop: true,
      backdropClass: 'cdk-overlay-transparent-backdrop',
      scrollStrategy: this.overlay.scrollStrategies.reposition(),
    });

    const data: TaskPreviewModalData = {
      task: event.task,
      boards: this.boards,
      employees: this.employees,
      tags: this.tags.map(t => t.name),
      properties: this.properties,
    };

    const popoverInjector = Injector.create({
      parent: this.injector,
      providers: [
        {provide: MAT_DIALOG_DATA, useValue: data},
        {provide: MatDialogRef, useValue: null},
      ],
    });

    const portal = new ComponentPortal(TaskPreviewModalComponent, null, popoverInjector);
    const componentRef = this.previewOverlayRef.attach(portal);

    componentRef.instance.usePopoverMode = true;
    componentRef.instance.popoverClose.pipe(takeUntil(this.destroy$)).subscribe(result => {
      this.closePreviewOverlay();
      if (result === 'edit') {
        this.openEditModal(event);
      } else if (result === 'reload') {
        this.weekGrid?.clearSelection();
        this.loadTasks();
      } else {
        this.weekGrid?.clearSelection();
      }
    });

    this.previewOverlayRef.backdropClick().pipe(takeUntil(this.destroy$)).subscribe(() => {
      this.closePreviewOverlay();
      this.weekGrid?.clearSelection();
    });
  }

  private closePreviewOverlay() {
    this.previewOverlayRef?.dispose();
    this.previewOverlayRef = null;
  }

  private openEditModal(event: {task: CalendarTaskLayoutModel; cellLeft: number; cellRight: number; slotTop: number}) {
    // Close any existing popover
    if (this.createOverlayRef) {
      this.createOverlayRef.dispose();
      this.createOverlayRef = null;
    }

    const positions = this.buildPopoverPositions(event.cellLeft, event.cellRight);
    const anchorX = this.pickAnchorX(event.cellLeft, event.cellRight);

    const positionStrategy = this.overlay
      .position()
      .flexibleConnectedTo({x: anchorX, y: event.slotTop})
      .withPositions(positions)
      .withPush(true)
      .withViewportMargin(8);

    this.createOverlayRef = this.overlay.create({
      positionStrategy,
      hasBackdrop: true,
      backdropClass: 'cdk-overlay-transparent-backdrop',
      scrollStrategy: this.overlay.scrollStrategies.reposition(),
    });

    const task = event.task;
    const data: TaskCreateEditModalData = {
      task,
      date: task.taskDate,
      startHour: task.startHour,
      boards: this.boards,
      employees: this.employees,
      tags: this.tags.map(t => t.name),
      propertyId: task.propertyId,
      properties: this.properties,
    };

    const popoverInjector = Injector.create({
      parent: this.injector,
      providers: [
        {provide: MAT_DIALOG_DATA, useValue: data},
        {provide: MatDialogRef, useValue: null},
      ],
    });

    const portal = new ComponentPortal(TaskCreateEditModalComponent, null, popoverInjector);
    const componentRef = this.createOverlayRef.attach(portal);

    componentRef.instance.usePopoverMode = true;
    componentRef.instance.timeChanged.pipe(takeUntil(this.destroy$)).subscribe(time => {
      this.weekGrid?.updateTaskTime(task.id, time.startHour, time.endHour);
    });
    componentRef.instance.popoverClose.pipe(takeUntil(this.destroy$)).subscribe(result => {
      this.closeCreateOverlay();
      if (result) this.loadTasks();
    });

    this.createOverlayRef.backdropClick().pipe(takeUntil(this.destroy$)).subscribe(() => {
      this.closeCreateOverlay();
    });
  }

  private buildPopoverPositions(cellLeft: number, cellRight: number): ConnectedPosition[] {
    const modalWidth = 500;
    const spaceRight = window.innerWidth - cellRight;
    const openToRight = spaceRight >= modalWidth + 16;
    return openToRight
      ? [
          {originX: 'start', originY: 'top', overlayX: 'start', overlayY: 'top', offsetX: 8},
          {originX: 'start', originY: 'top', overlayX: 'start', overlayY: 'bottom'},
        ]
      : [
          {originX: 'start', originY: 'top', overlayX: 'end', overlayY: 'top', offsetX: -8},
          {originX: 'start', originY: 'top', overlayX: 'end', overlayY: 'bottom'},
        ];
  }

  private pickAnchorX(cellLeft: number, cellRight: number): number {
    const modalWidth = 500;
    const spaceRight = window.innerWidth - cellRight;
    return spaceRight >= modalWidth + 16 ? cellRight : cellLeft;
  }

  private toLocalDateString(d: Date): string {
    const y = d.getFullYear();
    const m = (d.getMonth() + 1).toString().padStart(2, '0');
    const day = d.getDate().toString().padStart(2, '0');
    return `${y}-${m}-${day}`;
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
