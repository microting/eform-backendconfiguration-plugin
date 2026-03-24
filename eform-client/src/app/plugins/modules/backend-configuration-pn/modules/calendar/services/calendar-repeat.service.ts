import {Injectable} from '@angular/core';
import {CalendarRepeatMeta} from '../../../models/calendar';

export interface RepeatSelectOption {
  value: string;
  label: string;
  meta?: CalendarRepeatMeta;
}

/**
 * Port of getAllOccurrencesFromMeta(), getCustomOccurrenceCopies(),
 * and getRepeatSelectHtmlForDate() from JS.js ~line 2398.
 * Pure TypeScript — no DOM, no API calls.
 */
@Injectable({providedIn: 'root'})
export class CalendarRepeatService {

  private getMondayOfWeek(d: Date): Date {
    const date = new Date(d);
    const day = date.getDay();
    const diff = day === 0 ? -6 : 1 - day;
    date.setDate(date.getDate() + diff);
    date.setHours(0, 0, 0, 0);
    return date;
  }

  private dayOffsetFromMonday(weekday: number): number {
    return weekday === 0 ? 6 : weekday - 1;
  }

  private trimOccurrencesByEnd(raw: number[], meta: CalendarRepeatMeta): number[] {
    const unique = [...new Set(raw.map(t => {
      const d = new Date(t);
      d.setHours(0, 0, 0, 0);
      return d.getTime();
    }))].sort((a, b) => a - b);

    if (meta.endMode === 'after') return unique.slice(0, meta.afterCount ?? 10);
    if (meta.endMode === 'until' && meta.untilTs != null) {
      const until = new Date(meta.untilTs);
      until.setHours(23, 59, 59, 999);
      return unique.filter(t => t <= until.getTime());
    }
    return unique.slice(0, 250);
  }

  getAllOccurrences(meta: CalendarRepeatMeta, firstTs: number): number[] {
    const start = new Date(firstTs);
    start.setHours(0, 0, 0, 0);
    const raw: number[] = [];

    switch (meta.kind) {
      case 'daily':
        for (let i = 0; i < 400; i++) {
          const d = new Date(start);
          d.setDate(d.getDate() + i);
          raw.push(d.getTime());
        }
        break;

      case 'everyNd':
        for (let i = 0; i < 200; i++) {
          const d = new Date(start);
          d.setDate(d.getDate() + i * (meta.n ?? 1));
          raw.push(d.getTime());
        }
        break;

      case 'weeklyOne': {
        const W = meta.weekday ?? 1;
        const d = new Date(start);
        while (d.getDay() !== W) d.setDate(d.getDate() + 1);
        for (let i = 0; i < 120; i++) {
          raw.push(new Date(d).getTime());
          d.setDate(d.getDate() + 7);
        }
        break;
      }

      case 'weeklyMulti': {
        const days = meta.weekdays ?? [];
        const d = new Date(start);
        for (let g = 0; g < 600; g++) {
          if (days.includes(d.getDay())) raw.push(new Date(d).getTime());
          d.setDate(d.getDate() + 1);
        }
        break;
      }

      case 'weeklyAll': {
        const d = new Date(start);
        for (let g = 0; g < 600; g++) {
          raw.push(new Date(d).getTime());
          d.setDate(d.getDate() + 1);
        }
        break;
      }

      case 'everyNWeekOne': {
        const anchor = this.getMondayOfWeek(start);
        const W = meta.weekday ?? 1;
        const step = meta.n ?? 1;
        for (let wk = 0; wk < 150; wk += step) {
          const mon = new Date(anchor);
          mon.setDate(mon.getDate() + wk * 7);
          const tgt = new Date(mon);
          tgt.setDate(mon.getDate() + this.dayOffsetFromMonday(W));
          if (tgt.getTime() >= start.getTime()) raw.push(tgt.getTime());
        }
        break;
      }

      case 'everyNWeekAll': {
        const anchor = this.getMondayOfWeek(start);
        const step = meta.n ?? 1;
        for (let wk = 0; wk < 100; wk += step) {
          const mon = new Date(anchor);
          mon.setDate(mon.getDate() + wk * 7);
          for (let i = 0; i < 7; i++) {
            const tgt = new Date(mon);
            tgt.setDate(mon.getDate() + i);
            if (tgt.getTime() >= start.getTime()) raw.push(tgt.getTime());
          }
        }
        break;
      }

      case 'everyNWeekMulti': {
        const anchor = this.getMondayOfWeek(start);
        const step = meta.n ?? 1;
        const days = meta.weekdays ?? [];
        for (let wk = 0; wk < 100; wk += step) {
          const mon = new Date(anchor);
          mon.setDate(mon.getDate() + wk * 7);
          for (let i = 0; i < 7; i++) {
            const tgt = new Date(mon);
            tgt.setDate(mon.getDate() + i);
            if (tgt.getTime() >= start.getTime() && days.includes(tgt.getDay()))
              raw.push(tgt.getTime());
          }
        }
        break;
      }

      case 'monthlyDom': {
        let iter = new Date(start.getFullYear(), start.getMonth(), 1);
        for (let i = 0; i < 120; i++) {
          const last = new Date(iter.getFullYear(), iter.getMonth() + 1, 0).getDate();
          const day = Math.min(meta.dom ?? 1, last);
          const t = new Date(iter.getFullYear(), iter.getMonth(), day);
          if (t.getTime() >= start.getTime()) raw.push(t.getTime());
          iter.setMonth(iter.getMonth() + 1);
        }
        break;
      }

      case 'everyNMonthDom': {
        const dom = meta.dom ?? 1;
        const n = meta.n ?? 1;
        for (let i = 0; i < 100; i++) {
          const cand = new Date(start.getFullYear(), start.getMonth() + i * n, 1);
          const lastD = new Date(cand.getFullYear(), cand.getMonth() + 1, 0).getDate();
          const t = new Date(cand.getFullYear(), cand.getMonth(), Math.min(dom, lastD));
          if (t.getTime() >= start.getTime()) raw.push(t.getTime());
        }
        break;
      }

      case 'yearlyOne': {
        const mo = meta.month ?? 0;
        const dom = meta.dom ?? 1;
        for (let k = 0; k < 40; k++) {
          const yy = start.getFullYear() + k;
          const last = new Date(yy, mo + 1, 0).getDate();
          const t = new Date(yy, mo, Math.min(dom, last));
          if (t.getTime() >= start.getTime()) raw.push(t.getTime());
        }
        break;
      }

      case 'everyNYear': {
        const mo = meta.month ?? 0;
        const dom = meta.dom ?? 1;
        const n = meta.n ?? 1;
        for (let k = 0; k < 40; k++) {
          const yy = start.getFullYear() + k * n;
          const last = new Date(yy, mo + 1, 0).getDate();
          const t = new Date(yy, mo, Math.min(dom, last));
          if (t.getTime() >= start.getTime()) raw.push(t.getTime());
        }
        break;
      }
    }

    return this.trimOccurrencesByEnd(raw, meta);
  }

  /**
   * Build the repeat dropdown options for a given base date.
   * Mirrors getRepeatSelectHtmlForDate() from JS.js.
   */
  buildRepeatSelectOptions(date: Date): RepeatSelectOption[] {
    const dayNames = ['søndag', 'mandag', 'tirsdag', 'onsdag', 'torsdag', 'fredag', 'lørdag'];
    const monthNames = [
      'januar', 'februar', 'marts', 'april', 'maj', 'juni',
      'juli', 'august', 'september', 'oktober', 'november', 'december',
    ];

    const weekday = date.getDay();
    const dom = date.getDate();
    const month = date.getMonth();
    const dayName = dayNames[weekday];
    const monthName = monthNames[month];

    return [
      {value: 'none', label: 'Ingen gentagelse'},
      {
        value: 'daily',
        label: 'Dagligt',
        meta: {kind: 'daily', endMode: 'never'},
      },
      {
        value: 'weeklyOne',
        label: `Ugentligt på ${dayName}`,
        meta: {kind: 'weeklyOne', weekday, endMode: 'never'},
      },
      {
        value: 'weeklyAll',
        label: 'Alle dage i ugen',
        meta: {kind: 'weeklyAll', endMode: 'never'},
      },
      {
        value: 'monthlyDom',
        label: `Månedligt den ${dom}.`,
        meta: {kind: 'monthlyDom', dom, endMode: 'never'},
      },
      {
        value: 'yearlyOne',
        label: `Årligt den ${dom}. ${monthName}`,
        meta: {kind: 'yearlyOne', dom, month, endMode: 'never'},
      },
      {value: 'custom', label: 'Tilpasset…'},
    ];
  }

  /** Convert a custom repeat config to a CalendarRepeatMeta */
  buildMetaFromCustomConfig(
    step: number,
    unit: string,
    weekdays: number[],
    endMode: 'never' | 'after' | 'until',
    afterCount?: number,
    untilTs?: number
  ): CalendarRepeatMeta {
    const base: Partial<CalendarRepeatMeta> = {endMode, afterCount, untilTs, n: step};

    if (unit === 'day') {
      return {...base, kind: step === 1 ? 'daily' : 'everyNd'} as CalendarRepeatMeta;
    } else if (unit === 'week') {
      const kind = weekdays.length === 0 ? 'everyNWeekAll'
        : weekdays.length === 1 ? (step === 1 ? 'weeklyOne' : 'everyNWeekOne')
          : (step === 1 ? 'weeklyMulti' : 'everyNWeekMulti');
      return {
        ...base,
        kind,
        weekday: weekdays.length === 1 ? weekdays[0] : undefined,
        weekdays: weekdays.length !== 1 ? weekdays : undefined,
      } as CalendarRepeatMeta;
    } else if (unit === 'month') {
      return {...base, kind: step === 1 ? 'monthlyDom' : 'everyNMonthDom', dom: 1} as CalendarRepeatMeta;
    } else {
      return {...base, kind: step === 1 ? 'yearlyOne' : 'everyNYear', dom: 1, month: 0} as CalendarRepeatMeta;
    }
  }
}
