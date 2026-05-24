import {Injectable} from '@angular/core';
import {CalendarTaskLayoutModel, CalendarTaskModel} from '../../../models/calendar';

// Overlap factor for the equal-divide-with-overlap layout. Each card in a
// conflict group of N gets width = colW * OVERLAP_FACTOR / N, so adjacent
// cards overlap their neighbours. 1.8 matches Google Calendar's density —
// measured from screenshots in 2026-05-24 design spec.
const OVERLAP_FACTOR = 1.8;

@Injectable({providedIn: 'root'})
export class CalendarLayoutService {

  /**
   * Given a flat list of tasks for a single day, assign _left/_width/_zIndex
   * so overlapping tasks render in Google's cascade-with-overlap style.
   */
  computeLayout(tasks: CalendarTaskModel[]): CalendarTaskLayoutModel[] {
    if (!tasks || tasks.length === 0) return [];

    const events: CalendarTaskLayoutModel[] = tasks
      .slice()
      .sort((a, b) => a.startHour - b.startHour)
      .map(t => ({...t, _left: 0, _width: 100, _zIndex: 10}));

    // Build conflict groups: any pair that overlaps in time joins the same group.
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
      const n = group.length;
      if (n === 1) {
        group[0]._left = 0;
        group[0]._width = 100;
        group[0]._zIndex = 10;
        return;
      }
      const cardWidthPct = 100 * OVERLAP_FACTOR / n;
      const stepPct = (100 - cardWidthPct) / (n - 1);
      group.forEach((ev, i) => {
        ev._left = i * stepPct;
        ev._width = cardWidthPct;
        ev._zIndex = 10 + i;
      });
    });

    return events;
  }
}
