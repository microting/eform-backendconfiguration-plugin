export interface CustomRepeatConfig {
  step: number;
  unit: 'day' | 'week' | 'month' | 'year';
  weekdays: number[];        // 0=Sun … 6=Sat
  endMode: 'never' | 'after' | 'until';
  afterCount?: number;
  untilTs?: number;
}
