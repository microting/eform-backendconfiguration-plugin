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

    // Phase B: within each cluster, pack tasks into columns via first-fit, then
    // feed the SAME cascade geometry the corrected inputs. A cluster whose tasks
    // all mutually overlap (identical start+duration) yields columnIndex === list
    // index and numCols === cluster size, i.e. byte-identical to the old output.
    groups.forEach(group => {
      // Sort by start ascending, tie-break longer duration first (deterministic:
      // the longer task lands in the earlier column when two share a start).
      const ordered = group
        .slice()
        .sort((a, b) =>
          a.startHour - b.startHour || (b.duration || 1) - (a.duration || 1));

      // First-fit column packing. Each column holds its placed tasks in start
      // order; a task reuses the first column whose LAST task ends at or before
      // this task's start (touching counts as free — not an overlap). Because
      // tasks are start-sorted and only appended when they clear the last task,
      // each column's ends are monotonically non-decreasing, so the last task
      // alone determines the column's availability.
      const columns: CalendarTaskLayoutModel[][] = [];
      ordered.forEach(ev => {
        const openColumn = columns.find(col => {
          const last = col[col.length - 1];
          const lastEnd = last.startHour + (last.duration || 1);
          return lastEnd <= ev.startHour;
        });
        if (openColumn) {
          openColumn.push(ev);
        } else {
          columns.push([ev]);
        }
      });

      const numCols = columns.length;
      // Flag multi-card clusters so click-to-raise works even for the leftmost
      // card (whose _width === 100 is indistinguishable from a solo card).
      // Keyed on cluster size, identical to the old `n > 1`.
      const inGroup = group.length > 1;
      columns.forEach((col, columnIndex) => {
        const left = (columnIndex / numCols) * 100;
        col.forEach(ev => {
          ev._left = left;
          ev._width = 100 - left; // extend to the right edge, behind cards in front
          ev._zIndex = 10 + columnIndex;
          ev._inGroup = inGroup;
        });
      });
    });

    return events;
  }
}
