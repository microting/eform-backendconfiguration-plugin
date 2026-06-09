import {Injectable} from '@angular/core';
import {CalendarTaskLayoutModel, CalendarTaskModel} from '../../../models/calendar';

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
      .map(t => ({...t, _left: 0, _width: 100, _zIndex: 10, _inGroup: false}));

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
      // Cascade-with-overlap: each card in a conflict group of N starts at an
      // equal 100/N step (left = i * 100/N) but extends all the way to the
      // right edge (width = 100 - left), so cards behind run underneath the
      // ones in front. The rightmost card (highest i) sits on top and shows
      // fully; each earlier card reveals a 100/N strip before the next one
      // overlaps it. For N === 1 this collapses to a single full-width card
      // at left 0.
      const cardWidthPct = 100 / n;
      group.forEach((ev, i) => {
        ev._left = i * cardWidthPct;
        ev._width = 100 - ev._left; // extend to the right edge, behind the cards in front
        ev._zIndex = 10 + i;
        // Flag multi-card overlap groups so click-to-raise works even for the
        // leftmost card (whose _width === 100 is indistinguishable from solo).
        ev._inGroup = n > 1;
      });
    });

    return events;
  }
}
