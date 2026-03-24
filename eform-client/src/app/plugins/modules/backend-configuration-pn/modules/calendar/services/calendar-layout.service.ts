import {Injectable} from '@angular/core';
import {CalendarTaskLayoutModel, CalendarTaskModel} from '../../../models/calendar';

/**
 * Port of layoutDayEvents() from JS.js ~line 2020.
 * Pure date arithmetic — no DOM, no API calls.
 */
@Injectable({providedIn: 'root'})
export class CalendarLayoutService {

  /**
   * Given a flat list of tasks for a single day, assign _colIndex and _colCount
   * so overlapping tasks are rendered side-by-side.
   */
  computeLayout(tasks: CalendarTaskModel[]): CalendarTaskLayoutModel[] {
    if (!tasks || tasks.length === 0) return [];

    const events: CalendarTaskLayoutModel[] = tasks
      .slice()
      .sort((a, b) => a.startHour - b.startHour)
      .map(t => ({...t, _colIndex: 0, _colCount: 1}));

    // Build conflict groups
    const groups: CalendarTaskLayoutModel[][] = [];
    let currentGroup: CalendarTaskLayoutModel[] = [];
    let currentGroupEnd = -Infinity;

    events.forEach(ev => {
      const evEnd = ev.startHour + (ev.duration || 1);
      if (currentGroup.length === 0 || ev.startHour < currentGroupEnd) {
        currentGroup.push(ev);
        currentGroupEnd = Math.max(currentGroupEnd, evEnd);
      } else {
        groups.push(currentGroup);
        currentGroup = [ev];
        currentGroupEnd = evEnd;
      }
    });
    if (currentGroup.length > 0) groups.push(currentGroup);

    groups.forEach(group => {
      const columns: number[] = []; // each entry: endTime of last occupant
      group.forEach(ev => {
        let colIndex = 0;
        while (colIndex < columns.length && columns[colIndex] > ev.startHour) {
          colIndex++;
        }
        columns[colIndex] = ev.startHour + (ev.duration || 1);
        ev._colIndex = colIndex;
      });
      const maxCols = columns.length;
      group.forEach(ev => ev._colCount = maxCols);
    });

    return events;
  }
}
