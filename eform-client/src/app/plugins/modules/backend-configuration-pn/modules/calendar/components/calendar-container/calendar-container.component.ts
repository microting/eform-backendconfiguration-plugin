import {Component, Injector, OnDestroy, OnInit, ViewChild} from '@angular/core';
import {Overlay, OverlayRef, ConnectedPosition} from '@angular/cdk/overlay';
import {ComponentPortal} from '@angular/cdk/portal';
import {Subject} from 'rxjs';
import {takeUntil} from 'rxjs/operators';
import {Store} from '@ngrx/store';
import {selectCurrentUserIsAdmin} from 'src/app/state/auth/auth.selector';
import {
  BackendConfigurationPnCalendarService,
  BackendConfigurationPnPropertiesService,
} from '../../../../services';
import {
  CalendarBoardModel,
  CalendarRepeatRule,
  CalendarTaskLayoutModel,
  CalendarTaskModel,
} from '../../../../models/calendar';
import {CommonDictionaryModel, SharedTagModel, TemplateRequestModel} from 'src/app/common/models';
import {EFormService} from 'src/app/common/services';
import {CalendarLayoutService} from '../../services/calendar-layout.service';
import {CalendarStateService} from '../store';
import {MAT_DIALOG_DATA, MatDialog, MatDialogRef} from '@angular/material/dialog';
import {TaskCreateEditModalComponent, TaskCreateEditModalData} from '../../modals/task-create-edit-modal/task-create-edit-modal.component';
import {CalendarWeekGridComponent} from '../calendar-week-grid/calendar-week-grid.component';
import {TaskPreviewModalComponent, TaskPreviewModalData} from '../../modals/task-preview-modal/task-preview-modal.component';
import {ItemsPlanningPnTagsService} from 'src/app/plugins/modules/items-planning-pn/services';
import {EformTagService} from 'src/app/common/services';
import {BoardCreateModalComponent, BoardCreateModalData} from '../../modals/board-create-modal/board-create-modal.component';
import {BoardDeleteModalComponent, BoardDeleteModalData} from '../../modals/board-delete-modal/board-delete-modal.component';
import {RepeatScopeModalComponent} from '../../modals/repeat-scope-modal/repeat-scope-modal.component';
import {dialogConfigHelper} from 'src/app/common/helpers';
import {RepeatEditScope} from '../../../../models/calendar';

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
  allDayTasksByDay: CalendarTaskModel[][] = Array.from({length: 7}, () => []);

  eforms: {id: number; label: string}[] = [];
  logboegerFolderId: number | null = null;

  get tagNames(): string[] { return this.tags.map(t => t.name); }

  currentPropertyId: number | null = null;
  currentDate: string = (() => { const d = new Date(); return `${d.getFullYear()}-${(d.getMonth()+1).toString().padStart(2,'0')}-${d.getDate().toString().padStart(2,'0')}`; })();
  viewMode: 'week' | 'day' | 'schedule' = 'week';
  activeBoardIds: number[] = [];
  activeSiteIds: number[] = [];
  activeTeamIds: number[] = [];
  activeTagNames: string[] = [];
  sidebarOpen = true;
  isAdmin = false;

  constructor(
    private overlay: Overlay,
    private injector: Injector,
    private calendarService: BackendConfigurationPnCalendarService,
    private propertiesService: BackendConfigurationPnPropertiesService,
    private layoutService: CalendarLayoutService,
    private stateService: CalendarStateService,
    private tagsService: ItemsPlanningPnTagsService,
    private eformTagService: EformTagService,
    private eformService: EFormService,
    private dialog: MatDialog,
    private store: Store,
  ) {
    this.store.select(selectCurrentUserIsAdmin).pipe(takeUntil(this.destroy$))
      .subscribe(isAdmin => this.isAdmin = isAdmin);
  }

  ngOnInit(): void {
    this.stateService.filters$.pipe(takeUntil(this.destroy$)).subscribe(filters => {
      this.currentPropertyId = filters.propertyId;
      this.currentDate = filters.currentDate;
      this.viewMode = filters.viewMode;
      this.activeBoardIds = filters.activeBoardIds;
      this.activeSiteIds = filters.activeSiteIds;
      this.activeTeamIds = filters.activeTeamIds;
      this.activeTagNames = filters.activeTagNames;
      this.sidebarOpen = filters.sidebarOpen;
    });

    this.loadProperties();
    this.loadTags();
    this.loadTeams();
    this.loadEforms();
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  private findFolderByName(folders: any[], nameFragment: string): any {
    if (!folders) return null;
    for (const f of folders) {
      if (f && f.name && f.name.includes(nameFragment)) return f;
      if (f && f.children && f.children.length > 0) {
        const hit = this.findFolderByName(f.children, nameFragment);
        if (hit) return hit;
      }
    }
    return null;
  }

  loadEforms() {
    const req = new TemplateRequestModel();
    req.sort = 'Id';
    req.isSortDsc = false;
    req.pageSize = 1000;
    this.eformService.getAll(req).subscribe(res => {
      if (res && res.success && res.model) {
        this.eforms = res.model.templates.map(t => ({id: t.id, label: t.label}));
      }
    });
  }

  loadProperties() {
    this.propertiesService.getAllPropertiesDictionary().subscribe(res => {
      if (res && res.success) {
        this.properties = res.model;
        if (this.properties.length > 0 && !this.currentPropertyId) {
          this.onPropertySelected(this.properties[0].id);
        }
      }
    });
  }

  onPropertySelected(propertyId: number | null) {
    this.stateService.updatePropertyId(propertyId);
    if (propertyId) {
      this.loadBoards(propertyId, true);
      this.loadEmployees();
    } else {
      this.employees = [];
    }
  }

  loadBoards(propertyId: number, autoSelectDefault = false) {
    this.calendarService.getBoards(propertyId).subscribe(res => {
      if (res && res.success) {
        this.boards = res.model;
        if (autoSelectDefault && this.boards.length > 0) {
          const defaultBoard = this.boards.reduce((min, b) => b.id < min.id ? b : min);
          this.stateService.setActiveBoardIds([defaultBoard.id]);
        }
        this.propertiesService.getLinkedFolderDtos(propertyId).subscribe(folderRes => {
          if (folderRes && folderRes.success) {
            const logFolder = this.findFolderByName(folderRes.model, 'Logbøger');
            this.logboegerFolderId = logFolder ? logFolder.id : null;
          }
        });
        this.loadTasks();
      }
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

  loadTeams() {
    this.eformTagService.getAvailableTags().subscribe(res => {
      if (res && res.success) this.teams = res.model;
    });
  }

  onCreateTeam(name: string) {
    this.eformTagService.createTag({name} as any).subscribe(res => {
      if (res && res.success) this.loadTeams();
    });
  }

  onUpdateTeam(event: {id: number; name: string}) {
    this.eformTagService.updateTag({id: event.id, name: event.name, description: null} as any).subscribe(res => {
      if (res && res.success) this.loadTeams();
    });
  }

  onDeleteTeam(teamId: number) {
    this.eformTagService.deleteTag(teamId).subscribe(res => {
      if (res && res.success) this.loadTeams();
    });
  }

  loadEmployees() {
    this.propertiesService.getDeviceUsersFiltered({
      propertyIds: this.currentPropertyId ? [this.currentPropertyId] : [],
      nameFilter: '',
      sort: 'Name',
      isSortDsc: false,
      showResigned: false,
      tagIds: this.activeTeamIds,
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
        this.activeSiteIds,
      )
      .subscribe(res => {
        if (res && res.success) {
          this.tasks = (res.model || []).map((t: any) => ({
            ...t,
            repeatRule: this.mapRepeatType(t.repeatType ?? 0, t.repeatEvery ?? 1),
          }));
          this.rebuildLayout(monday);
        }
      });
  }

  private hourToTimeStr(hour: number): string {
    const h = Math.floor(hour);
    const m = Math.round((hour % 1) * 60);
    return `${h.toString().padStart(2, '0')}:${m.toString().padStart(2, '0')}`;
  }

  private rebuildLayout(monday: Date) {
    const boardColorMap = new Map(this.boards.map(b => [b.id, b.color]));
    this.tasksByDay = Array.from({length: 7}, () => []);
    this.allDayTasksByDay = Array.from({length: 7}, () => []);
    this.tasks.forEach(task => {
      const taskDate = new Date(task.taskDate);
      taskDate.setHours(0, 0, 0, 0);
      const mondayCopy = new Date(monday);
      mondayCopy.setHours(0, 0, 0, 0);
      const dayIdx = Math.round((taskDate.getTime() - mondayCopy.getTime()) / 86400000);
      if (dayIdx >= 0 && dayIdx < 7) {
        const color = (task.boardId && boardColorMap.get(task.boardId)) || task.color;
        const startText = task.startText || this.hourToTimeStr(task.startHour);
        const endText = task.endText || this.hourToTimeStr(task.startHour + task.duration);
        if (task.isAllDay) {
          this.allDayTasksByDay[dayIdx].push({...task, color, startText, endText});
        } else {
          this.tasksByDay[dayIdx].push({...task, color, startText, endText, _colIndex: 0, _colCount: 1});
        }
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
      eforms: this.eforms,
      folderId: this.logboegerFolderId,
      planningTags: this.tags.map(t => ({id: t.id, name: t.name})),
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

  onCreateBoard() {
    if (!this.currentPropertyId) return;
    const dialogRef = this.dialog.open(BoardCreateModalComponent, {
      data: {propertyId: this.currentPropertyId} as BoardCreateModalData,
      width: '400px',
    });
    dialogRef.afterClosed().subscribe(result => {
      if (result && this.currentPropertyId) {
        this.loadBoards(this.currentPropertyId);
      }
    });
  }

  onUpdateBoard(event: {id: number; name: string; color: string}) {
    this.calendarService.updateBoard(event).subscribe(res => {
      if (res && res.success && this.currentPropertyId) {
        this.loadBoards(this.currentPropertyId);
        this.loadTasks();
      }
    });
  }

  onDeleteBoard(board: CalendarBoardModel) {
    const dialogRef = this.dialog.open(BoardDeleteModalComponent, {
      data: {board} as BoardDeleteModalData,
      width: '400px',
    });
    dialogRef.afterClosed().subscribe(result => {
      if (result && this.currentPropertyId) {
        this.loadBoards(this.currentPropertyId);
      }
    });
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

  onTeamToggled(teamId: number) {
    this.stateService.toggleTeam(teamId);
    this.loadEmployees();
  }

  onEmployeeToggled(siteId: number) {
    this.stateService.toggleSite(siteId);
    this.loadTasks();
  }

  onTaskMoved(event: {taskId: number; newDate: string; newStartHour: number; repeatSeriesId?: string; originalDate: string}) {
    const task = this.tasks.find(t => t.id === event.taskId);
    const isRepeating = task && task.repeatRule && task.repeatRule !== 'none';

    const doMove = (scope?: RepeatEditScope) => {
      const obs = isRepeating
        ? this.calendarService.moveTaskWithScope(event.taskId, event.newDate, event.newStartHour, scope ?? 'this', event.originalDate)
        : this.calendarService.moveTask(event.taskId, event.newDate, event.newStartHour);
      obs.subscribe(res => {
        if (res && res.success) this.loadTasks();
      });
    };

    if (isRepeating) {
      const ref = this.dialog.open(
        RepeatScopeModalComponent,
        dialogConfigHelper(this.overlay, {mode: 'move'})
      );
      ref.afterClosed().subscribe(scope => {
        if (scope) {
          doMove(scope);
        } else {
          this.loadTasks();
        }
      });
    } else {
      doMove();
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
      } else if (result === 'copy') {
        this.openCopyModal(event);
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
      eforms: this.eforms,
      folderId: this.logboegerFolderId,
      planningTags: this.tags.map(t => ({id: t.id, name: t.name})),
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

  private openCopyModal(event: {task: CalendarTaskLayoutModel; cellLeft: number; cellRight: number; slotTop: number}) {
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

    const sourceTask = event.task;
    const data: TaskCreateEditModalData = {
      task: null,
      sourceTask,
      date: sourceTask.taskDate,
      startHour: sourceTask.startHour,
      boards: this.boards,
      employees: this.employees,
      tags: this.tags.map(t => t.name),
      propertyId: sourceTask.propertyId,
      properties: this.properties,
      eforms: this.eforms,
      folderId: this.logboegerFolderId,
      planningTags: this.tags.map(t => ({id: t.id, name: t.name})),
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

  private mapRepeatType(repeatType: number, repeatEvery: number): CalendarRepeatRule {
    if (!repeatType || repeatType === 0) return 'none';
    switch (repeatType) {
      case 1: return repeatEvery === 1 ? 'daily' : 'custom';
      case 2: return repeatEvery === 1 ? 'weekly' : 'custom';
      case 3: return repeatEvery === 1 ? 'monthly' : 'custom';
      default: return 'none';
    }
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
