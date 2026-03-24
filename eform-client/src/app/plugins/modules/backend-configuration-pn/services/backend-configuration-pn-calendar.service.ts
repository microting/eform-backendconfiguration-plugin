import {Injectable} from '@angular/core';
import {Observable, of} from 'rxjs';
import {OperationDataResult, OperationResult} from 'src/app/common/models';
import {
  CalendarBoardModel,
  CalendarRepeatMeta,
  CalendarTaskCreateModel,
  CalendarTaskModel,
  CalendarTaskUpdateModel,
  RepeatDeleteScope,
  RepeatEditScope,
} from '../models';

const TASKS_KEY = 'cal_tasks';
const BOARDS_KEY = 'cal_boards';
const NEXT_ID_KEY = 'cal_next_id';

@Injectable({providedIn: 'root'})
export class BackendConfigurationPnCalendarService {
  constructor() {
    this.seed();
  }

  getTasksForWeek(
    propertyId: number,
    weekStart: string,
    weekEnd: string,
    boardIds: number[],
    tagNames: string[]
  ): Observable<OperationDataResult<CalendarTaskModel[]>> {
    let tasks = this.loadTasks().filter(
      t => t.taskDate >= weekStart && t.taskDate <= weekEnd
    );
    if (boardIds.length) tasks = tasks.filter(t => boardIds.includes(t.boardId));
    if (tagNames.length) tasks = tasks.filter(t => t.tags.some(tag => tagNames.includes(tag)));
    return of({success: true, message: '', model: tasks});
  }

  createTask(model: CalendarTaskCreateModel): Observable<OperationResult> {
    const tasks = this.loadTasks();
    const seriesId = crypto.randomUUID();
    const occurrences = this.expandOccurrences(model, seriesId);
    tasks.push(...occurrences);
    this.saveTasks(tasks);
    return of({success: true, message: ''});
  }

  updateTask(model: CalendarTaskUpdateModel, scope: RepeatEditScope): Observable<OperationResult> {
    let tasks = this.loadTasks();
    if (scope === 'this') {
      tasks = tasks.map(t =>
        t.id === model.id ? this.toStoredTask(model, t.repeatSeriesId) : t
      );
    } else if (scope === 'all') {
      tasks = tasks.map(t =>
        t.repeatSeriesId && t.repeatSeriesId === model.repeatSeriesId
          ? this.toStoredTask({...model, taskDate: t.taskDate}, t.repeatSeriesId)
          : t
      );
    } else {
      tasks = tasks.map(t =>
        t.repeatSeriesId && t.repeatSeriesId === model.repeatSeriesId && t.taskDate >= model.taskDate
          ? this.toStoredTask({...model, taskDate: t.taskDate}, t.repeatSeriesId)
          : t
      );
    }
    this.saveTasks(tasks);
    return of({success: true, message: ''});
  }

  deleteTask(id: number, scope: RepeatDeleteScope): Observable<OperationResult> {
    let tasks = this.loadTasks();
    const task = tasks.find(t => t.id === id);
    if (!task) return of({success: true, message: ''});
    if (scope === 'this') {
      tasks = tasks.filter(t => t.id !== id);
    } else if (scope === 'all') {
      tasks = tasks.filter(t => !(task.repeatSeriesId && t.repeatSeriesId === task.repeatSeriesId));
    } else {
      tasks = tasks.filter(
        t => !(task.repeatSeriesId && t.repeatSeriesId === task.repeatSeriesId && t.taskDate >= task.taskDate)
      );
    }
    this.saveTasks(tasks);
    return of({success: true, message: ''});
  }

  moveTask(id: number, newDate: string, newStartHour: number): Observable<OperationResult> {
    const tasks = this.loadTasks().map(t => {
      if (t.id !== id) return t;
      return {
        ...t,
        taskDate: newDate,
        startHour: newStartHour,
        startText: this.formatHour(newStartHour),
        endText: this.formatHour(newStartHour + t.dur),
      };
    });
    this.saveTasks(tasks);
    return of({success: true, message: ''});
  }

  moveTaskWithScope(
    id: number,
    newDate: string,
    newStartHour: number,
    scope: 'this' | 'thisAndFollowing' | 'all'
  ): Observable<OperationResult> {
    const allTasks = this.loadTasks();
    const target = allTasks.find(t => t.id === id);
    if (!target || !target.repeatSeriesId) {
      return this.moveTask(id, newDate, newStartHour);
    }

    // Calculate the offset (in days and hours)
    const oldDate = new Date(target.taskDate + 'T00:00:00');
    const newDateObj = new Date(newDate + 'T00:00:00');
    const dayOffset = Math.round((newDateObj.getTime() - oldDate.getTime()) / 86400000);
    const hourOffset = newStartHour - target.startHour;

    const applyOffset = (t: CalendarTaskModel): CalendarTaskModel => {
      const d = new Date(t.taskDate + 'T00:00:00');
      d.setDate(d.getDate() + dayOffset);
      const y = d.getFullYear();
      const m = (d.getMonth() + 1).toString().padStart(2, '0');
      const day = d.getDate().toString().padStart(2, '0');
      const movedDate = `${y}-${m}-${day}`;
      const movedHour = t.startHour + hourOffset;
      return {
        ...t,
        taskDate: movedDate,
        startHour: movedHour,
        startText: this.formatHour(movedHour),
        endText: this.formatHour(movedHour + t.dur),
      };
    };

    const seriesId = target.repeatSeriesId;
    const tasks = allTasks.map(t => {
      if (t.repeatSeriesId !== seriesId) return t;
      if (scope === 'this' && t.id !== id) return t;
      if (scope === 'thisAndFollowing' && t.taskDate < target.taskDate) return t;
      return applyOffset(t);
    });

    this.saveTasks(tasks);
    return of({success: true, message: ''});
  }

  toggleComplete(id: number, completed: boolean): Observable<OperationResult> {
    const tasks = this.loadTasks().map(t => (t.id === id ? {...t, completed} : t));
    this.saveTasks(tasks);
    return of({success: true, message: ''});
  }

  getBoards(propertyId: number): Observable<OperationDataResult<CalendarBoardModel[]>> {
    return of({success: true, message: '', model: this.loadBoards()});
  }

  createBoard(model: Omit<CalendarBoardModel, 'id'>): Observable<OperationResult> {
    const boards = this.loadBoards();
    boards.push({...model, id: this.nextId()});
    this.saveBoards(boards);
    return of({success: true, message: ''});
  }

  updateBoard(model: CalendarBoardModel): Observable<OperationResult> {
    this.saveBoards(this.loadBoards().map(b => (b.id === model.id ? model : b)));
    return of({success: true, message: ''});
  }

  deleteBoard(id: number): Observable<OperationResult> {
    this.saveBoards(this.loadBoards().filter(b => b.id !== id));
    return of({success: true, message: ''});
  }

  getTags(propertyId: number): Observable<OperationDataResult<string[]>> {
    const unique = [...new Set(this.loadTasks().flatMap(t => t.tags))];
    return of({success: true, message: '', model: unique});
  }

  private loadTasks(): CalendarTaskModel[] {
    try {
      return JSON.parse(localStorage.getItem(TASKS_KEY) || '[]');
    } catch {
      return [];
    }
  }

  private saveTasks(tasks: CalendarTaskModel[]): void {
    localStorage.setItem(TASKS_KEY, JSON.stringify(tasks));
  }

  private loadBoards(): CalendarBoardModel[] {
    try {
      return JSON.parse(localStorage.getItem(BOARDS_KEY) || '[]');
    } catch {
      return [];
    }
  }

  private saveBoards(boards: CalendarBoardModel[]): void {
    localStorage.setItem(BOARDS_KEY, JSON.stringify(boards));
  }

  private nextId(): number {
    const current = parseInt(localStorage.getItem(NEXT_ID_KEY) || '0', 10);
    const next = current + 1;
    localStorage.setItem(NEXT_ID_KEY, String(next));
    return next;
  }

  private formatHour(h: number): string {
    const totalMinutes = Math.round(h * 60);
    const hours = Math.floor(totalMinutes / 60) % 24;
    const minutes = totalMinutes % 60;
    return `${hours.toString().padStart(2, '0')}:${minutes.toString().padStart(2, '0')}`;
  }

  private toStoredTask(
    model: CalendarTaskUpdateModel | (CalendarTaskCreateModel & {id?: number}),
    seriesId?: string
  ): CalendarTaskModel {
    const id = (model as CalendarTaskUpdateModel).id ?? this.nextId();
    return {
      id,
      title: model.title,
      startHour: model.startHour,
      dur: model.dur,
      startText: this.formatHour(model.startHour),
      endText: this.formatHour(model.startHour + model.dur),
      tags: model.tags,
      assigneeIds: model.assigneeIds,
      boardId: model.boardId,
      color: model.color,
      descriptionHtml: model.descriptionHtml,
      repeatRule: model.repeatRule,
      repeatMeta: model.repeatMeta,
      taskDate: model.taskDate,
      repeatSeriesId: seriesId,
      completed: false,
      driveLink: model.driveLink,
      propertyId: model.propertyId,
    };
  }

  private expandOccurrences(model: CalendarTaskCreateModel, seriesId: string): CalendarTaskModel[] {
    if (model.repeatRule === 'none') {
      return [{
        id: this.nextId(),
        title: model.title,
        startHour: model.startHour,
        dur: model.dur,
        startText: this.formatHour(model.startHour),
        endText: this.formatHour(model.startHour + model.dur),
        tags: model.tags,
        assigneeIds: model.assigneeIds,
        boardId: model.boardId,
        color: model.color,
        descriptionHtml: model.descriptionHtml,
        repeatRule: model.repeatRule,
        repeatMeta: model.repeatMeta,
        taskDate: model.taskDate,
        repeatSeriesId: undefined,
        completed: false,
        driveLink: model.driveLink,
        propertyId: model.propertyId,
      }];
    }
    return this.generateDates(model).map(date => ({
      id: this.nextId(),
      title: model.title,
      startHour: model.startHour,
      dur: model.dur,
      startText: this.formatHour(model.startHour),
      endText: this.formatHour(model.startHour + model.dur),
      tags: model.tags,
      assigneeIds: model.assigneeIds,
      boardId: model.boardId,
      color: model.color,
      descriptionHtml: model.descriptionHtml,
      repeatRule: model.repeatRule,
      repeatMeta: model.repeatMeta,
      taskDate: date,
      repeatSeriesId: seriesId,
      completed: false,
      driveLink: model.driveLink,
      propertyId: model.propertyId,
    }));
  }

  private generateDates(model: CalendarTaskCreateModel): string[] {
    const start = new Date(model.taskDate + 'T00:00:00');
    const dates: string[] = [];
    const MAX = 104;
    const push = (d: Date) => {
      const y = d.getFullYear();
      const m = (d.getMonth() + 1).toString().padStart(2, '0');
      const day = d.getDate().toString().padStart(2, '0');
      dates.push(`${y}-${m}-${day}`);
    };

    if (model.repeatRule === 'daily') {
      const cur = new Date(start);
      for (let i = 0; i < 90 && dates.length < MAX; i++, cur.setDate(cur.getDate() + 1)) {
        push(new Date(cur));
      }
    } else if (model.repeatRule === 'weekly') {
      const cur = new Date(start);
      for (let i = 0; i < 52 && dates.length < MAX; i++, cur.setDate(cur.getDate() + 7)) {
        push(new Date(cur));
      }
    } else if (model.repeatRule === 'weekdays') {
      const cur = new Date(start);
      while (dates.length < MAX) {
        const dow = cur.getDay();
        if (dow >= 1 && dow <= 5) push(new Date(cur));
        cur.setDate(cur.getDate() + 1);
        if (dates.length >= 52 * 5) break;
      }
    } else if (model.repeatRule === 'monthly') {
      const cur = new Date(start);
      for (let i = 0; i < 12 && dates.length < MAX; i++, cur.setMonth(cur.getMonth() + 1)) {
        push(new Date(cur));
      }
    } else if (model.repeatRule === 'yearly') {
      const cur = new Date(start);
      for (let i = 0; i < 3 && dates.length < MAX; i++, cur.setFullYear(cur.getFullYear() + 1)) {
        push(new Date(cur));
      }
    } else if (model.repeatRule === 'custom') {
      return this.generateCustomDates(model, start);
    }
    return dates;
  }

  private generateCustomDates(model: CalendarTaskCreateModel, start: Date): string[] {
    const meta = model.repeatMeta as (CalendarRepeatMeta & {step?: number; unit?: string}) | undefined;
    if (!meta) return [model.taskDate];
    const dates: string[] = [];
    const MAX = 104;
    const endDate = meta.endMode === 'until' && meta.untilTs ? new Date(meta.untilTs) : null;
    const maxCount = meta.endMode === 'after' && meta.afterCount ? Math.min(meta.afterCount, MAX) : MAX;
    const step = meta.step ?? 1;
    const unit = meta.unit ?? 'week';
    const cur = new Date(start);

    while (dates.length < maxCount) {
      if (endDate && cur > endDate) break;
      dates.push(cur.toISOString().split('T')[0]);
      if (unit === 'day') cur.setDate(cur.getDate() + step);
      else if (unit === 'week') cur.setDate(cur.getDate() + step * 7);
      else if (unit === 'month') cur.setMonth(cur.getMonth() + step);
      else if (unit === 'year') cur.setFullYear(cur.getFullYear() + step);
      else break;
    }
    return dates;
  }

  private seed(): void {
    if (localStorage.getItem(TASKS_KEY) !== null || localStorage.getItem(BOARDS_KEY) !== null) {
      return;
    }
    const boardId = this.nextId();
    this.saveBoards([{id: boardId, name: 'Standard', color: '#2196F3', propertyId: 1}]);
    const today = new Date().toISOString().split('T')[0];
    this.saveTasks([
      {
        id: this.nextId(), title: 'Morgenmøde', startHour: 9, dur: 1,
        startText: '09:00', endText: '10:00', tags: [], assigneeIds: [],
        boardId, color: '#2196F3', descriptionHtml: '', repeatRule: 'none',
        taskDate: today, completed: false, propertyId: 1,
      },
      {
        id: this.nextId(), title: 'Ugentlig rapport', startHour: 14, dur: 1.5,
        startText: '14:00', endText: '15:30', tags: [], assigneeIds: [],
        boardId, color: '#2196F3', descriptionHtml: '', repeatRule: 'none',
        taskDate: today, completed: false, propertyId: 1,
      },
    ]);
  }
}
